using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;
using JsonSerializer = System.Text.Json.JsonSerializer;
public class HomeController : Controller
{
    private readonly AgentSandboxService _sandboxSvc;
    private readonly AppConfig _cfg;
    private readonly SessionStateManager _sessionMgr; //新增

    public HomeController(AgentSandboxService svc, SessionStateManager sessionMgr)
    {
        _sandboxSvc = svc;
        _sessionMgr = sessionMgr;
    }

    public IActionResult Index()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        ViewBag.SessionId = sessionId;
        return View();
    }

    // 前端页面调用：发送Prompt给Coze
    [HttpPost]
    public async Task<IActionResult> SendChat([FromBody] ChatSendDto dto)
    {
        //var resText = await _sandboxSvc.SendChat(dto);

        //// 需要人工审批场景
        //if (resText.StartsWith("__TOOL_APPROVE__"))
        //{
        //    var jsonPart = resText.Substring("__TOOL_APPROVE__".Length);

        //    return Content(jsonPart);
        //}

        //return Content(resText);

        var session = _sessionMgr.GetOrCreate(dto.SessionId);

        //========== 分支1：按钮确认新建草稿订单 ==========
        if (dto.Action == "ConfirmAddDraft")
        {
            if (session.Flow != SessionFlow.AddDraftConfirm || session.PendingOrder == null)
            {
                return Content("当前没有待确认的订单，请先发起新建订单。");
            }

            // 直接调用底层工具执行建单（含去重保护）
            using var conn = _sandboxSvc.GetConnection();
            var orderId = session.PendingOrder.OrderId;
            if (string.IsNullOrEmpty(orderId))
            {
                var today = DateTime.Now.ToString("yyyyMMdd");
                var sqlMax = $"SELECT order_id FROM orders WHERE order_id LIKE 'ORD{today}%' ORDER BY order_id DESC LIMIT 1";
                using var cmdMax = new Microsoft.Data.Sqlite.SqliteCommand(sqlMax, conn);
                var lastId = cmdMax.ExecuteScalar() as string;
                var seq = lastId != null ? int.Parse(lastId.Substring(10)) + 1 : 1;
                orderId = $"ORD{today}{seq:D3}";
                session.PendingOrder.OrderId = orderId;
            }
            var existCmd = new Microsoft.Data.Sqlite.SqliteCommand(
                "SELECT 1 FROM orders WHERE order_id = @oid LIMIT 1", conn);
            existCmd.Parameters.AddWithValue("@oid", orderId);
            var exists = existCmd.ExecuteScalar() != null;
            if (!exists)
            {
                using var tx = conn.BeginTransaction();
                var resObj = await _sandboxSvc.RunAddDraftInner(session.PendingOrder!, conn, tx);
                tx.Commit();
            }
            _sessionMgr.Clear(dto.SessionId);
            return Content($"订单 {orderId} 已创建（或已存在），无需重复操作。");
        }

        //========== 分支2：用户打字输入“正确/确认”兜底 ==========
        var input = dto.Prompt.Trim();

        var confirmWords = new HashSet<string> { "正确", "确认", "是的", "没错", "对" };

        if (confirmWords.Contains(input))
        {
            if (session.Flow == SessionFlow.AddDraftConfirm && session.PendingOrder != null)
            {
                using var conn = _sandboxSvc.GetConnection();
                var orderId2 = session.PendingOrder.OrderId;
                if (string.IsNullOrEmpty(orderId2))
                {
                    var today = DateTime.Now.ToString("yyyyMMdd");
                    var sqlMax = $"SELECT order_id FROM orders WHERE order_id LIKE 'ORD{today}%' ORDER BY order_id DESC LIMIT 1";
                    using var cmdMax = new Microsoft.Data.Sqlite.SqliteCommand(sqlMax, conn);
                    var lastId = cmdMax.ExecuteScalar() as string;
                    var seq = lastId != null ? int.Parse(lastId.Substring(10)) + 1 : 1;
                    orderId2 = $"ORD{today}{seq:D3}";
                    session.PendingOrder.OrderId = orderId2;
                }
                var existCmd2 = new Microsoft.Data.Sqlite.SqliteCommand(
                    "SELECT 1 FROM orders WHERE order_id = @oid LIMIT 1", conn);
                existCmd2.Parameters.AddWithValue("@oid", orderId2);
                if (existCmd2.ExecuteScalar() == null)
                {
                    using var tx = conn.BeginTransaction();
                    var resObj = await _sandboxSvc.RunAddDraftInner(session.PendingOrder!, conn, tx);
                    tx.Commit();
                }
                _sessionMgr.Clear(dto.SessionId);
                return Content($"订单 {orderId2} 已创建（或已存在）。");
            }
        }

        //========== 下面完全是你原来的老代码，一字不动 ==========
        var resText = await _sandboxSvc.SendChat(dto, _sessionMgr);

        // =========【新增】提取 {{PAYLOAD:xxx}} 存入会话状态 =========
        var match = Regex.Match(resText, @"\{\{PAYLOAD:(.+?)\}\}\}", RegexOptions.Singleline);

        if (match.Success)
        {
            var jsonPayload = match.Groups[1].Value;

            try
            {
                var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var payload = System.Text.Json.JsonSerializer.Deserialize<AddDraftOrderPayload>(jsonPayload, opt);

                if (payload != null)
                {
                    _sessionMgr.SetAddDraftConfirm(dto.SessionId, payload);
                }
            }
            catch
            {
                //解析失败忽略
            }
        }

        // 把payload标记从返回给前端的文本中剔除
        var displayText = Regex.Replace(resText, @"\{\{PAYLOAD:.+?\}\}\}", "", RegexOptions.Singleline);

        // 需要人工审批场景
        if (displayText.StartsWith("__TOOL_APPROVE__"))
        {
            var jsonPart = displayText.Substring("__TOOL_APPROVE__".Length);
            return Content(jsonPart);
        }

        // add_draft 参数不足：Bot 收到提示后会带着真实明细重新发起工具调用
        if (displayText.Contains("缺少 items 参数"))
        {
            // 保存已提取的 payload，供下次确认使用
            if (match.Success)
            {
                var jsonPayload = match.Groups[1].Value;
                try
                {
                    var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var payload = System.Text.Json.JsonSerializer.Deserialize<AddDraftOrderPayload>(jsonPayload, opt);
                    if (payload != null) _sessionMgr.SetAddDraftConfirm(dto.SessionId, payload);
                }
                catch { }
            }
            return Content(
                "已收到订单信息，请提供商品明细后我为您建单。\n" +
                "请在下次消息中明确说明商品名称、数量和单价。"
            );
        }

        return Content(displayText);
    }

    /// <summary>
    /// SSE流式聊天接口
    /// url: /Home/SseChat?sessionId=xxx&prompt=xxx
    /// </summary>
    [HttpGet("SseChat")]
    public async Task SseChat(string sessionId, string prompt, CancellationToken cancellationToken)
    {
        Response.Headers["Content‑Type"] = "text/event‑stream; charset=utf‑8";
        Response.Headers["Cache‑Control"] = "no‑cache";
        Response.Headers["Connection"] = "keep‑alive";
        Response.Headers["X‑Accel‑Buffering"] = "no";

        if (string.IsNullOrWhiteSpace(prompt))
        {
            await Response.WriteAsync("data:{\"error\":\"prompt为空\"}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return;
        }

        // 优先使用已存储的扣子真实 conversation_id，保持上下文连贯
        var session = _sessionMgr.GetOrCreate(sessionId);
        var cozeConvId = string.IsNullOrEmpty(session.CozeConvId) ? sessionId : session.CozeConvId;

        try
        {
            await _sandboxSvc.StreamChatAsync(
                _cfg.CozeBotId,
                cozeConvId,
                sessionId,
                prompt,
                Response.Body,
                cancellationToken,
                _sessionMgr,
                sessionId
            );
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            //浏览器断开连接，忽略
        }
        catch (Exception ex)
        {
            await Response.WriteAsync(
                $"data:{System.Text.Json.JsonSerializer.Serialize(new { Error = ex.Message })}\n\n",
                cancellationToken
            );
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    /// <summary>
    /// ✅ Coze插件【唯一入口】POST /api/tool/call
    /// tool_name: query_order / add_draft / batch_update / delete_record / tool_approve / tool_rollback
    /// </summary>
    [HttpPost("api/tool/call")]
    public async Task<IActionResult> CozeToolCall([FromBody] ToolRequest req)
    {
        Dictionary<string, object> realPayload;
        try
        {
            realPayload = ConvertToDict(req.Payload);
        }
        catch (Exception ex)
        {
            return new JsonResult(new ToolResponse { Code = 500, Msg = "payload解析异常：" + ex.Message })
            {
                ContentType = "application/json; charset=utf-8"
            };
        }

        ToolResponse output;
        switch (req.ToolName)
        {
            //✅ query_order 现在统一走HandleToolCall，不再写模拟代码
            case "query_order":
            case "add_draft":
            case "batch_update":
            case "delete_record":
                {
                    var innerDto = new ToolCallDto
                    {
                        SessionId = req.SessionId,
                        ToolName = req.ToolName,
                        Params = realPayload
                    };
                    var innerResp = await _sandboxSvc.HandleToolCall(innerDto);
                    output = new ToolResponse
                    {
                        Code = innerResp.Code,
                        Msg = innerResp.Msg,
                        Data = innerResp.Data,
                        ApproveToken = innerResp.ApproveToken,
                        LogId = innerResp.LogId
                    };
                    if (innerResp.Code == 1001 && !string.IsNullOrEmpty(innerResp.ApproveToken))
                    {
                        // 待人工审批场景：保存上下文，不提交结果，等待网页审批按钮触发submit
                        await _sandboxSvc.SaveTicketCozeContext(innerResp.ApproveToken, req.ChatId, req.ConvId, req.ToolCallId);
                    }
                    else
                    {
                        // L0/L1 无需审批工具，执行完毕立刻把结果提交给扣子，驱动对话继续
                        await _sandboxSvc.SubmitToolOutputAsync(req.ConvId, req.ChatId, req.ToolCallId, innerResp);
                    }
                    break;
                }
            case "tool_approve":
                {
                    if (!realPayload.TryGetValue("approve_token", out var tkObj) || tkObj is not string token)
                    {
                        output = new ToolResponse { Code = 400, Msg = "payload缺少 approve_token" };
                        break;
                    }
                    var ticket = await _sandboxSvc.GetTicket(token);
                    if (ticket == null)
                    {
                        output = new ToolResponse { Code = 404, Msg = "工单不存在或已过期" };
                        break;
                    }
                    var innerDto = new ToolCallDto
                    {
                        SessionId = ticket.SessionId,
                        ToolName = ticket.ToolName,
                        Params = ticket.Params,
                        ApproveToken = token
                    };
                    var innerResp = await _sandboxSvc.HandleToolCall(innerDto);
                    output = new ToolResponse
                    {
                        Code = innerResp.Code,
                        Msg = innerResp.Msg,
                        Data = innerResp.Data,
                        ApproveToken = innerResp.ApproveToken,
                        LogId = innerResp.LogId
                    };
                    break;
                }
            case "tool_rollback":
                {
                    if (!realPayload.TryGetValue("log_id", out var logObj) || logObj is not string logId)
                    {
                        output = new ToolResponse { Code = 400, Msg = "payload缺少 log_id" };
                        break;
                    }
                    var innerResp = await _sandboxSvc.Rollback(logId);
                    output = new ToolResponse
                    {
                        Code = innerResp.Code,
                        Msg = innerResp.Msg,
                        Data = innerResp.Data,
                        ApproveToken = innerResp.ApproveToken,
                        LogId = innerResp.LogId
                    };
                    break;
                }
            default:
                output = new ToolResponse { Code = -1, Msg = $"未知tool_name:{req.ToolName}" };
                break;
        }
        return new JsonResult(output) { ContentType = "application/json; charset=utf-8" };
    }

    // ========= 下面两个接口【只给Razor前端页面调用】Coze插件不要调用 =========
    [HttpPost("api/tool/rollback")]
    public async Task<IActionResult> Rollback([FromQuery] string logId)
    {
        var r = await _sandboxSvc.Rollback(logId);
        return Json(r);
    }

    /// <summary>
    /// 网页点击审批按钮：执行业务，同时调用扣子submit_tool_output，通知智能体继续执行
    /// </summary>
    [HttpPost("api/tool/approve")]
    public async Task<IActionResult> Approve([FromQuery] string approveToken)
    {
        var ticket = await _sandboxSvc.GetTicket(approveToken);

        if (ticket == null)
            return Json(new { Code = 404, Msg = "审批工单不存在或已过期" });

        var callDto = new ToolCallDto
        {
            SessionId = ticket.SessionId,
            ToolName = ticket.ToolName,
            Params = ticket.Params,
            ApproveToken = approveToken
        };

        var resp = await _sandboxSvc.HandleToolCall(callDto);

        // ✅回传给扣子，让requires_action状态继续执行智能体
        if (!string.IsNullOrEmpty(ticket.ChatId) && !string.IsNullOrEmpty(ticket.ConvId) && !string.IsNullOrEmpty(ticket.ToolCallId))
        {
            // 参数顺序：convId，chatId，toolCallId 【千万不能颠倒】
            await _sandboxSvc.SubmitToolOutputAsync(ticket.ConvId, ticket.ChatId, ticket.ToolCallId, resp);
            await _sandboxSvc.DeleteTicket(approveToken);
        }

        return Json(resp);
    }

    private Dictionary<string, object> ConvertToDict(JsonElement ele)
    {
        // 情况1：传进来本身就是json字符串（扣子string类型payload）
        if (ele.ValueKind == JsonValueKind.String)
        {
            string jsonStr = ele.GetString()!;
            return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonStr)!;
        }
        // 情况2：传进来就是json对象（标准Object类型payload）
        else if (ele.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(ele.GetRawText())!;
        }
        throw new Exception("payload格式错误，必须是JSON对象或者JSON字符串");
    }

    [HttpGet("PollChatResult")]
    public async Task<IActionResult> PollChatResult(string conversationId)
    {
        var (isFinish, answer, isRequireAction) = await _sandboxSvc.PollChatResult(conversationId);
        return Json(new
        {
            isFinish,
            answer,
            isRequireAction
        });
    }

}
