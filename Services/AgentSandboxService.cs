using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

public class AgentSandboxService
{
    private readonly AppConfig _cfg;
    private readonly IHttpClientFactory _httpFactory;
    public AgentSandboxService(AppConfig cfg, IHttpClientFactory httpFactory)
    {
        _cfg = cfg;
        _httpFactory = httpFactory;
    }

    private readonly Dictionary<string, ToolPolicy> _policy = new()
    {
        {"query_order", new ToolPolicy{Level="L0", NeedApprove=false, NeedSandbox=false}},
        {"add_draft", new ToolPolicy{Level="L1", NeedApprove=false, NeedSandbox=true}},
        {"batch_update", new ToolPolicy{Level="L2", NeedApprove=true, NeedSandbox=true}},
        {"delete_record", new ToolPolicy{Level="L2", NeedApprove=true, NeedSandbox=true}},
    };

    /// <summary>获取sqlite连接，开启WAL、外键</summary>
    private SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection($"Data Source={_cfg.SqliteLogPath}");
        conn.Open();
        //开启WAL并发、外键约束
        using var cmd = new SqliteCommand("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", conn);
        cmd.ExecuteNonQuery();
        return conn;
    }

    #region 扣子OpenApi发送对话（原有逻辑不变）

    public async Task<string> SendChat(ChatSendDto dto)
    {
        var token = _cfg.CozeApiToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return "CozeApiToken配置为空";
        }
        var cozeChatUrl = "https://api.coze.cn/v3/chat";
        var reqBody = new
        {
            bot_id = _cfg.CozeBotId,
            user_id = dto.SessionId,
            stream = false,
            auto_save_history = true,
            additional_messages = new[]
            {
            new { role = "user", content = dto.Prompt, content_type = "text" }
        }
        };
        var jsonStr = System.Text.Json.JsonSerializer.Serialize(reqBody);
        var clientChat = _httpFactory.CreateClient();
        var postMsg = new HttpRequestMessage(HttpMethod.Post, cozeChatUrl);
        postMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        postMsg.Content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
        var respPost = await clientChat.SendAsync(postMsg);
        var respPostText = await respPost.Content.ReadAsStringAsync();
        var postResult = JsonDocument.Parse(respPostText);
        if (postResult.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ToString() != "0")
        {
            return $"创建会话失败：{respPostText}";
        }
        var dataEl = postResult.RootElement.GetProperty("data");
        var chatId = dataEl.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
        var convId = dataEl.TryGetProperty("conversation_id", out var convEl) ? convEl.ToString() : null;
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(convId))
        {
            return $"chat_id / conversation_id 获取失败：{respPostText}";
        }

        string status = "";
        JsonDocument? retrieveResp = null;
        bool isCompleted = false;

        for (int i = 0; i < 90; i++)
        {
            await Task.Delay(1200);
            var clientRet = _httpFactory.CreateClient();
            var qs = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://api.coze.cn/v3/chat/retrieve",
                new Dictionary<string, string> { ["conversation_id"] = convId!, ["chat_id"] = chatId! });
            var reqRet = new HttpRequestMessage(HttpMethod.Get, qs);
            reqRet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var retResp = await clientRet.SendAsync(reqRet);
            var retText = await retResp.Content.ReadAsStringAsync();
            retrieveResp = JsonDocument.Parse(retText);
            if (retrieveResp!.RootElement.TryGetProperty("code", out var rc) && rc.ToString() != "0")
                return $"retrieve查询失败：{retText}";
            var rdata = retrieveResp.RootElement.GetProperty("data");
            status = rdata.TryGetProperty("status", out var sEl) ? sEl.ToString()! : "";

            if (status == "completed")
            {
                isCompleted = true;
                break;
            }
            if (status is "failed" or "canceled")
            {
                return $"对话执行失败或被取消，状态：{status}";
            }

            if (status == "requires_action")
            {
                //解析工具调用
                var reqActionJson = rdata.GetProperty("required_action");
                if (!reqActionJson.TryGetProperty("submit_tool_outputs", out var submitObj)
                    || submitObj.ValueKind != JsonValueKind.Object)
                {
                    return $"requires_action解析失败，无submit_tool_outputs";
                }
                if (!submitObj.TryGetProperty("tool_calls", out var toolCallsEl)
                    || toolCallsEl.ValueKind != JsonValueKind.Array)
                {
                    return $"requires_action解析失败，tool_calls不是数组";
                }
                var toolCallList = toolCallsEl.EnumerateArray().ToList();
                if (toolCallList.Count == 0)
                {
                    return $"requires_action tool_calls数组为空";
                }
                var toolCallItem = toolCallList[0];
                if (toolCallItem.ValueKind != JsonValueKind.Object)
                {
                    return $"tool_call项不是对象";
                }

                var toolCallId = toolCallItem.GetProperty("id").GetString()!;
                var func = toolCallItem.GetProperty("function");
                var toolName = func.GetProperty("name").GetString()!;
                var argsJson = func.GetProperty("arguments").GetString()!;

                Dictionary<string, object> toolParams;
                try
                {
                    toolParams = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson)!;
                }
                catch (Exception ex)
                {
                    return $"工具参数arguments解析失败：{ex.Message}";
                }

                //执行本地工具
                var toolDto = new ToolCallDto
                {
                    SessionId = dto.SessionId,
                    ToolName = toolName,
                    Params = toolParams
                };
                var toolResp = await HandleToolCall(toolDto);

                //需要人工审批，返回标记
                if (toolResp.Code == 1001)
                {
                    return $"__TOOL_APPROVE__{System.Text.Json.JsonSerializer.Serialize(toolResp)}";
                }

                //==== Hack：不submit_tool_outputs，丢弃旧chat，新建对话传入tool结果获取AI回答 ====
                var clientNew = _httpFactory.CreateClient();
                var newChatBody = new
                {
                    bot_id = _cfg.CozeBotId,
                    user_id = dto.SessionId,
                    stream = false,
                    auto_save_history = true,
                    additional_messages = new object[]
                    {
                        new { role = "user", content = dto.Prompt, content_type = "text" },
                        new { role = "tool", tool_call_id = toolCallId, content = System.Text.Json.JsonSerializer.Serialize(toolResp), content_type = "text" }
                    }
                };
                var newJson = System.Text.Json.JsonSerializer.Serialize(newChatBody);
                var newMsg = new HttpRequestMessage(HttpMethod.Post, cozeChatUrl);
                newMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                newMsg.Content = new StringContent(newJson, Encoding.UTF8, "application/json");
                var newRespPost = await clientNew.SendAsync(newMsg);
                var newRespText = await newRespPost.Content.ReadAsStringAsync();
                var newPostResult = JsonDocument.Parse(newRespText);
                if (newPostResult.RootElement.TryGetProperty("code", out var newCodeEl) && newCodeEl.ToString() != "0")
                {
                    return $"新建后置对话失败：{newRespText}";
                }
                var newDataEl = newPostResult.RootElement.GetProperty("data");
                var newChatId = newDataEl.TryGetProperty("id", out var newIdEl) ? newIdEl.ToString() : null;
                var newConvId = newDataEl.TryGetProperty("conversation_id", out var newConvEl) ? newConvEl.ToString() : null;
                if (string.IsNullOrEmpty(newChatId) || string.IsNullOrEmpty(newConvId))
                {
                    return $"新chat获取id失败 {newRespText}";
                }

                //轮询全新chat
                string newStatus = "";
                bool newCompleted = false;

                for (int j = 0; j < 40; j++)
                {
                    await Task.Delay(1200);

                    var qs2 = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://api.coze.cn/v3/chat/retrieve",
                        new Dictionary<string, string> { ["conversation_id"] = newConvId!, ["chat_id"] = newChatId! });

                    var reqRetNew = new HttpRequestMessage(HttpMethod.Get, qs2);
                    reqRetNew.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    var retRespNew = await clientNew.SendAsync(reqRetNew);
                    var retTextNew = await retRespNew.Content.ReadAsStringAsync();
                    var retrieveRespNew = JsonDocument.Parse(retTextNew);
                    var rdataNew = retrieveRespNew.RootElement.GetProperty("data");
                    newStatus = rdataNew.TryGetProperty("status", out var sElNew) ? sElNew.ToString()! : "";

                    if (newStatus == "completed")
                    {
                        newCompleted = true;
                        break;
                    }
                    if (newStatus is "failed" or "canceled")
                    {
                        return $"后置对话失败，状态：{newStatus}";
                    }
                }
                if (!newCompleted)
                {
                    return $"后置对话超时，工具执行结果：{System.Text.Json.JsonSerializer.Serialize(toolResp)}";
                }

                //读取AI回答
                var msgQs = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://api.coze.cn/v3/chat/message/list",
                    new Dictionary<string, string> { ["conversation_id"] = newConvId!, ["chat_id"] = newChatId! });
                var reqMsg = new HttpRequestMessage(HttpMethod.Get, msgQs);
                reqMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var msgResp = await clientNew.SendAsync(reqMsg);
                var msgText = await msgResp.Content.ReadAsStringAsync();
                var msgResult = JsonDocument.Parse(msgText);
                var msgList = msgResult.RootElement.GetProperty("data");
                var answer = msgList.EnumerateArray()
                    .Where(m => m.TryGetProperty("role", out var r) && r.ToString() == "assistant"
                             && m.TryGetProperty("type", out var t) && t.ToString() == "answer")
                    .Select(m => m.TryGetProperty("content", out var c) ? c.ToString() : null)
                    .FirstOrDefault();
                return answer ?? "工具执行完成，无AI回复";
            }
        }

        if (!isCompleted)
        {
            return $"对话超时（最大48秒），最后状态：{status}";
        }

        var clientMsg = _httpFactory.CreateClient();
        var msgQsOrg = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://api.coze.cn/v3/chat/message/list",
            new Dictionary<string, string> { ["conversation_id"] = convId!, ["chat_id"] = chatId! });
        var reqMsgOrg = new HttpRequestMessage(HttpMethod.Get, msgQsOrg);
        reqMsgOrg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var msgRespOrg = await clientMsg.SendAsync(reqMsgOrg);
        var msgTextOrg = await msgRespOrg.Content.ReadAsStringAsync();
        var msgResultOrg = JsonDocument.Parse(msgTextOrg);
        var msgListOrg = msgResultOrg.RootElement.GetProperty("data");
        if (msgListOrg.ValueKind == System.Text.Json.JsonValueKind.Array
            && msgListOrg.EnumerateArray()
                .Any(m => m.TryGetProperty("role", out var r) && r.ToString() == "assistant"
                       && m.TryGetProperty("type", out var t) && t.ToString() == "answer"))
        {
            var ansOrg = msgListOrg.EnumerateArray()
                .Where(m => m.TryGetProperty("role", out var r) && r.ToString() == "assistant"
                         && m.TryGetProperty("type", out var t) && t.ToString() == "answer")
                .Select(m => m.TryGetProperty("content", out var c) ? c.ToString() : null)
                .FirstOrDefault();
            return ansOrg ?? "无返回内容";
        }
        return "未收到回复消息";
    }

    public async Task<string> SubmitToolOutputAsync(string convId, string chatId, string toolCallId, object outputData)
    {
        var token = _cfg.CozeApiToken?.Trim();
        var url = "https://api.coze.cn/v3/chat/submit_tool_outputs";
        var body = new
        {
            conversation_id = convId,
            chat_id = chatId,
            tool_outputs = new[] { new { tool_call_id = toolCallId, output = System.Text.Json.JsonSerializer.Serialize(outputData) } }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        var client = _httpFactory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await client.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    #endregion

    public async Task<ToolRespDto> HandleToolCall(ToolCallDto dto)
    {
        if (!_policy.TryGetValue(dto.ToolName, out var policy))
            return new ToolRespDto { Code = 400, Msg = "未注册工具" };
        var logId = Guid.NewGuid().ToString("N");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await CleanExpireData();

        //L0 查询，直接读库，不需要沙箱
        if (policy.Level == "L0")
        {
            using var conn = GetConnection();
            var queryResult = await RunQueryOrder(dto.Params, conn);
            await SaveLog(logId, dto, policy, null, null, queryResult, "auto_pass", null, ts);
            return new ToolRespDto { Code = 0, Msg = "查询成功", Data = queryResult, LogId = logId };
        }

        //高危操作，无审批token → 返回待审批工单
        if (policy.NeedApprove && string.IsNullOrEmpty(dto.ApproveToken))
        {
            var approveToken = Guid.NewGuid().ToString("N");
            var ticket = new ApproveTicket
            {
                LogId = logId,
                SessionId = dto.SessionId,
                ToolName = dto.ToolName,
                Params = dto.Params
            };
            var expireTs = ts + 3600;
            await SaveTicket(approveToken, ticket, expireTs, "", "", "");
            await SaveLog(logId, dto, policy, null, null, null, "pending", null, ts);
            return new ToolRespDto
            {
                Code = 1001,
                Msg = "高危操作等待人工审批",
                ApproveToken = approveToken,
                LogId = logId
            };
        }

        // ========== 有沙箱：先执行沙箱预演（事务回滚，数据不保存） ==========
        object? sandboxResult = null;

        if (policy.NeedSandbox)
        {
            using var sandboxConn = GetConnection();
            using var tx = sandboxConn.BeginTransaction();
            try
            {
                sandboxResult = await RunBusiness(dto.ToolName, dto.Params, sandboxConn, tx);
                tx.Rollback(); //沙箱全部执行完成，回滚事务，数据不落地
            }
            catch (Exception ex)
            {
                tx.Rollback();
                await SaveLog(logId, dto, policy, null, new { error = ex.Message }, null, "sandbox_fail", null, ts);
                return new ToolRespDto { Code = 400, Msg = $"沙箱预演失败:{ex.Message}", Data = sandboxResult, LogId = logId };
            }
        }

        // 执行快照：更新/删除前读取原始数据，用于后续回滚
        var snapshot = await MakeSnapshot(dto.ToolName, dto.Params);

        // ========== 正式生产执行，事务提交，真正修改订单表 ==========
        object prodResult;
        using var prodConn = GetConnection();
        using var prodTx = prodConn.BeginTransaction();

        try
        {
            prodResult = await RunBusiness(dto.ToolName, dto.Params, prodConn, prodTx);
            prodTx.Commit();
        }
        catch (Exception ex)
        {
            prodTx.Rollback();
            await SaveLog(logId, dto, policy, snapshot, sandboxResult, null, "prod_fail", null, ts);
            return new ToolRespDto { Code = 500, Msg = $"正式执行失败:{ex.Message}", LogId = logId };
        }

        var snapExpire = ts + 7 * 24 * 3600;
        await SaveSnapshot(logId, snapshot, snapExpire);
        await SaveLog(logId, dto, policy, snapshot, sandboxResult, prodResult, "approved", "admin", ts);

        return new ToolRespDto
        {
            Code = 0,
            Msg = "执行成功",
            Data = new { sandboxResult, prodResult },
            LogId = logId
        };
    }

    /// <summary>执行业务：新增草稿/更新/删除，同一个连接+事务</summary>
    private async Task<object> RunBusiness(string toolName, Dictionary<string, object> pars, SqliteConnection conn, SqliteTransaction tx)
    {
        return toolName switch
        {
            "add_draft" => await RunAddDraft(pars, conn, tx),
            "batch_update" => await RunBatchUpdate(pars, conn, tx),
            "delete_record" => await RunDeleteRecord(pars, conn, tx),
            _ => throw new NotSupportedException($"不支持工具:{toolName}")
        };
    }

    #region 各个业务工具实现

    /// <summary>将 Dictionary<string,object> 中的 JsonElement 值递归提取为纯 JSON 兼容值</summary>
    private static object ExtractJsonValues(object obj) => obj switch
    {
        JsonElement elem => elem.ValueKind switch
        {
            JsonValueKind.String => elem.GetString()!,
            JsonValueKind.Number => elem.GetDecimal(),
            JsonValueKind.False => false,
            JsonValueKind.True => true,
            JsonValueKind.Null => null,
            JsonValueKind.Object => elem.EnumerateObject().ToDictionary(
                p => p.Name, p => (object)ExtractJsonValues(p.Value)),
            JsonValueKind.Array => elem.EnumerateArray().Select(e => ExtractJsonValues(e)).ToList(),
            _ => throw new Exception($"不支持的JsonElement类型: {elem.ValueKind}")
        },
        _ => obj
    };

    /// <summary>从 JSON 字符串中按属性名获取值（简化版，避免引入Newtonsoft）</summary>
    private static string? GetJsonString(JsonElement root, string propName)
    {
        if (root.TryGetProperty(propName, out var el))
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
        return null;
    }

    /// <summary>L0 查询订单，只读不需要事务</summary>
    private async Task<object> RunQueryOrder(Dictionary<string, object> pars, SqliteConnection conn)
    {
        if (!pars.TryGetValue("order_id", out var oidObj) || oidObj is not string orderId)
            throw new Exception("缺少order_id参数");
        var dict = new Dictionary<string, object>();
        var cmd = new SqliteCommand(@"SELECT * FROM orders WHERE order_id=@oid", conn);
        cmd.Parameters.AddWithValue("@oid", orderId);
        using var reader = await cmd.ExecuteReaderAsync();
        OrderEntity? order = null;
        if (await reader.ReadAsync())
        {
            order = new OrderEntity
            {
                Id = (long)reader["id"],
                OrderId = reader["order_id"].ToString()!,
                TotalAmount = Convert.ToDecimal(reader["total_amount"]),
                Status = Convert.ToInt32(reader["status"]),
                CreateTime = reader["create_time"].ToString()!
            };
        }
        await reader.CloseAsync();
        if (order == null)
            return new { code = 404, msg = "订单不存在" };
        //查询订单明细
        var itemCmd = new SqliteCommand(@"SELECT * FROM order_item WHERE order_id=@pid", conn);
        itemCmd.Parameters.AddWithValue("@pid", order.Id);
        var itemList = new List<OrderItemEntity>();
        using var itemReader = await itemCmd.ExecuteReaderAsync();
        while (await itemReader.ReadAsync())
        {
            itemList.Add(new OrderItemEntity
            {
                Id = (long)itemReader["id"],
                OrderId = (long)itemReader["order_id"],
                ProductName = itemReader["product_name"].ToString()!,
                Price = Convert.ToDecimal(itemReader["price"]),
                Quantity = Convert.ToInt32(itemReader["quantity"])
            });
        }
        return new { code = 0, data = new { order, items = itemList } };
    }

    /// <summary>add_draft 新增草稿订单</summary>
    private async Task<object> RunAddDraft(Dictionary<string, object> pars, SqliteConnection conn, SqliteTransaction tx)
    {
        // ConvertToDict 的 value 是 JsonElement，Newtonsoft 会错误序列化为 ValueKind 元数据；
        // 用 System.Text.Json 提取真实值后再反序列化，避免此问题。
        var json = System.Text.Json.JsonSerializer.Serialize(ExtractJsonValues(pars));
        var payload = System.Text.Json.JsonSerializer.Deserialize<AddDraftOrderPayload>(json)!;

        string orderId = payload.OrderId?.Trim() ?? "";

        if (string.IsNullOrEmpty(orderId))
        {
            // 👉 改用当前conn+tx原生SQL查询，不碰db
            var today = DateTime.Now.ToString("yyyyMMdd");
            string sqlMax = @"
                SELECT order_id FROM orders 
                WHERE order_id LIKE @prefix
                ORDER BY order_id DESC LIMIT 1;
                ";
            var cmdMax = new SqliteCommand(sqlMax, conn, tx);
            cmdMax.Parameters.AddWithValue("@prefix", $"ORD{today}%");
            var lastOrderId = await cmdMax.ExecuteScalarAsync() as string;

            int newSeq = 1;
            if (!string.IsNullOrEmpty(lastOrderId) && lastOrderId.Length >= 11)
            {
                var seqStr = lastOrderId.Substring(10);
                if (int.TryParse(seqStr, out var seq))
                {
                    newSeq = seq + 1;
                }
            }
            orderId = $"ORD{today}{newSeq:D3}";
            payload.OrderId = orderId; // 回填到payload
        }

        // 👉 唯一性校验，同样原生SQL，不走db
        string sqlExist = "SELECT 1 FROM orders WHERE order_id = @oid LIMIT 1";
        var cmdExist = new SqliteCommand(sqlExist, conn, tx);
        cmdExist.Parameters.AddWithValue("@oid", payload.OrderId);
        var existObj = await cmdExist.ExecuteScalarAsync();
        if (existObj != null)
        {
            return new { code = -1, msg = "订单编号已存在" };
        }

        //插入订单主表
        var insertOrderCmd = new SqliteCommand(@"
        INSERT INTO orders(order_id,total_amount,status) VALUES(@oid,@amt,0);
        SELECT last_insert_rowid();", conn, tx);
        insertOrderCmd.Parameters.AddWithValue("@oid", payload.OrderId);
        insertOrderCmd.Parameters.AddWithValue("@amt", payload.TotalAmount);
        long newOrderPk = (long)(await insertOrderCmd.ExecuteScalarAsync())!;

        //插入订单明细
        foreach (var it in payload.Items)
        {
            var itemCmd = new SqliteCommand(@"
            INSERT INTO order_item(order_id,product_name,price,quantity)
            VALUES(@pid,@pname,@price,@qty)", conn, tx);
            itemCmd.Parameters.AddWithValue("@pid", payload.OrderId);
            itemCmd.Parameters.AddWithValue("@pname", it.ProductName);
            itemCmd.Parameters.AddWithValue("@price", it.Price);
            itemCmd.Parameters.AddWithValue("@qty", it.Quantity);
            await itemCmd.ExecuteNonQueryAsync();
        }

        return new { code = 0, msg = "草稿订单创建成功", db_id = newOrderPk, order_id = payload.OrderId };
    }

    /// <summary>batch_update 更新订单状态 target_id=order_id字符串</summary>
    private async Task<object> RunBatchUpdate(Dictionary<string, object> pars, SqliteConnection conn, SqliteTransaction tx)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ExtractJsonValues(pars));
        var payload = System.Text.Json.JsonSerializer.Deserialize<BatchUpdateOrderPayload>(json)!;
        if (string.IsNullOrWhiteSpace(payload.TargetId)) throw new Exception("target_id不能为空");
        var checkCmd = new SqliteCommand(@"SELECT id FROM orders WHERE order_id=@oid", conn, tx);
        checkCmd.Parameters.AddWithValue("@oid", payload.TargetId);
        var obj = await checkCmd.ExecuteScalarAsync();
        if (obj == null) throw new Exception($"订单 {payload.TargetId} 不存在");
        var updateCmd = new SqliteCommand(@"UPDATE orders SET status=@st WHERE order_id=@oid", conn, tx);
        updateCmd.Parameters.AddWithValue("@st", payload.Status);
        updateCmd.Parameters.AddWithValue("@oid", payload.TargetId);
        var affect = await updateCmd.ExecuteNonQueryAsync();
        return new { code = 0, msg = $"更新成功，影响行数{affect}", target_id = payload.TargetId, new_status = payload.Status };
    }

    /// <summary>delete_record 删除订单 target_id=order_id字符串</summary>
    private async Task<object> RunDeleteRecord(Dictionary<string, object> pars, SqliteConnection conn, SqliteTransaction tx)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ExtractJsonValues(pars));
        var payload = System.Text.Json.JsonSerializer.Deserialize<DeleteOrderPayload>(json)!;
        if (string.IsNullOrWhiteSpace(payload.TargetId)) throw new Exception("target_id不能为空");
        var checkCmd = new SqliteCommand(@"SELECT id FROM orders WHERE order_id=@oid", conn, tx);
        checkCmd.Parameters.AddWithValue("@oid", payload.TargetId);
        var pkObj = await checkCmd.ExecuteScalarAsync();
        if (pkObj == null) throw new Exception($"订单 {payload.TargetId} 不存在");
        //开启外键，删除主表自动级联删除order_item
        var delCmd = new SqliteCommand(@"DELETE FROM orders WHERE order_id=@oid", conn, tx);
        delCmd.Parameters.AddWithValue("@oid", payload.TargetId);
        var rows = await delCmd.ExecuteNonQueryAsync();
        return new { code = 0, msg = $"删除订单成功，影响行数{rows}", deleted_order_id = payload.TargetId };
    }
    #endregion

    /// <summary>删除/更新前，读取订单+明细快照，用于回滚</summary>
    private async Task<object?> MakeSnapshot(string toolName, Dictionary<string, object> pars)
    {
        if (toolName is not ("batch_update" or "delete_record")) return null;
        if (!pars.TryGetValue("target_id", out var tidObj) || tidObj is not string targetOrderId)
            return null;
        using var conn = GetConnection();
        var cmd = new SqliteCommand(@"SELECT * FROM orders WHERE order_id=@oid", conn);
        cmd.Parameters.AddWithValue("@oid", targetOrderId);
        OrderEntity? order = null;
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            order = new OrderEntity
            {
                Id = (long)reader["id"],
                OrderId = reader["order_id"].ToString()!,
                TotalAmount = Convert.ToDecimal(reader["total_amount"]),
                Status = Convert.ToInt32(reader["status"]),
                CreateTime = reader["create_time"].ToString()!
            };
        }
        await reader.CloseAsync();
        if (order == null) return null;
        //读明细
        var itemCmd = new SqliteCommand(@"SELECT * FROM order_item WHERE order_id=@pid", conn);
        itemCmd.Parameters.AddWithValue("@pid", order.Id);
        var itemList = new List<OrderItemEntity>();
        using var itemRd = await itemCmd.ExecuteReaderAsync();
        while (await itemRd.ReadAsync())
        {
            itemList.Add(new OrderItemEntity
            {
                Id = (long)itemRd["id"],
                OrderId = (long)itemRd["order_id"],
                ProductName = itemRd["product_name"].ToString()!,
                Price = Convert.ToDecimal(itemRd["price"]),
                Quantity = Convert.ToInt32(itemRd["quantity"])
            });
        }
        return new { order, items = itemList };
    }

    /// <summary>回滚：利用快照重建订单和明细</summary>
    public async Task<ToolRespDto> Rollback(string logId)
    {
        var snapJson = await GetSnapshotJson(logId);
        if (string.IsNullOrEmpty(snapJson))
            return new ToolRespDto { Code = 404, Msg = "快照过期，无法回滚" };
        var snapDoc = JsonDocument.Parse(snapJson);
        var orderEl = snapDoc.RootElement.GetProperty("order");
        var itemsEl = orderEl.TryGetProperty("items", out var iEl) ? iEl : default;

        // 提取快照字段值
        var snapOrderId = orderEl.GetProperty("OrderId").ToString();
        long snapId = orderEl.GetProperty("Id").GetInt64();
        decimal snapAmt = orderEl.GetProperty("TotalAmount").GetDecimal();
        int snapStatus = orderEl.GetProperty("Status").GetInt32();
        string snapTime = orderEl.GetProperty("CreateTime").ToString();

        // 使用同一连接和事务完成所有操作
        using var conn = GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            //先删除现在这条订单（如果还存在）
            var delCmd = new SqliteCommand(@"DELETE FROM orders WHERE order_id=@oid", conn, tx);
            delCmd.Parameters.AddWithValue("@oid", snapOrderId);
            await delCmd.ExecuteNonQueryAsync();
            //恢复订单主记录（使用快照原始ID，保留AUTOINCREMENT序列）
            var insOrder = new SqliteCommand(@"
                INSERT INTO orders(id,order_id,total_amount,status,create_time)
                VALUES(@id,@oid,@amt,@st,@ct)", conn, tx);
            insOrder.Parameters.AddWithValue("@id", snapId);
            insOrder.Parameters.AddWithValue("@oid", snapOrderId);
            insOrder.Parameters.AddWithValue("@amt", snapAmt);
            insOrder.Parameters.AddWithValue("@st", snapStatus);
            insOrder.Parameters.AddWithValue("@ct", snapTime);
            await insOrder.ExecuteNonQueryAsync();

            //恢复明细（使用快照原始ID）
            foreach (var jt in itemsEl.EnumerateArray())
            {
                var insItem = new SqliteCommand(@"
                    INSERT INTO order_item(id,order_id,product_name,price,quantity)
                    VALUES(@iid,@oid,@pname,@price,@qty)", conn, tx);
                insItem.Parameters.AddWithValue("@iid", jt.GetProperty("Id").GetInt64());
                insItem.Parameters.AddWithValue("@oid", jt.GetProperty("OrderId").GetInt64());
                insItem.Parameters.AddWithValue("@pname", jt.GetProperty("ProductName").ToString());
                insItem.Parameters.AddWithValue("@price", jt.GetProperty("Price").GetDecimal());
                insItem.Parameters.AddWithValue("@qty", jt.GetProperty("Quantity").GetInt32());
                await insItem.ExecuteNonQueryAsync();
            }
            //标记回滚记录
            var updSql = @"UPDATE tool_log SET is_rollback = 1, rollback_ts = $ts WHERE log_id = $lid";
            var updCmd = new SqliteCommand(updSql, conn, tx);
            updCmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            updCmd.Parameters.AddWithValue("$lid", logId);
            await updCmd.ExecuteNonQueryAsync();

            tx.Commit(); //仅全部完成后提交一次
            return new ToolRespDto { Code = 0, Msg = "回滚完成，订单及明细已恢复快照数据" };
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return new ToolRespDto { Code = 500, Msg = $"回滚失败：{ex.Message}" };
        }
    }

    #region 数据库表读写（tool_log / approve_ticket / snapshot_item，不变，适配GetConnection）

    private async Task SaveLog(string logId, ToolCallDto dto, ToolPolicy policy, object? snapshot, object? sandboxResult, object? prodResult, string status, string? approveUser, long ts)
    {
        using var conn = GetConnection();
        var sql = @"INSERT INTO tool_log
        (log_id, tool_name, risk_level, session_id, input_params, snapshot, sandbox_result, prod_result, approve_status, approve_user, create_ts)
        VALUES ($lid, $tname, $lv, $sid, $par, $snap, $sbr, $pr, $st, $auser, $ts)";
        var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("$lid", logId);
        cmd.Parameters.AddWithValue("$tname", dto.ToolName);
        cmd.Parameters.AddWithValue("$lv", policy.Level);
        cmd.Parameters.AddWithValue("$sid", dto.SessionId);
        cmd.Parameters.AddWithValue("$par", System.Text.Json.JsonSerializer.Serialize(dto.Params));
        cmd.Parameters.AddWithValue("$snap", snapshot == null ? (object)DBNull.Value : System.Text.Json.JsonSerializer.Serialize(snapshot));
        cmd.Parameters.AddWithValue("$sbr", sandboxResult == null ? (object)DBNull.Value : System.Text.Json.JsonSerializer.Serialize(sandboxResult));
        cmd.Parameters.AddWithValue("$pr", prodResult == null ? (object)DBNull.Value : System.Text.Json.JsonSerializer.Serialize(prodResult));
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$auser", approveUser == null ? (object)DBNull.Value : approveUser);
        cmd.Parameters.AddWithValue("$ts", ts);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SaveTicket(string token, ApproveTicket ticket, long expireTs, string chatId, string convId, string toolCallId)
    {
        using var conn = GetConnection();
        var sql = @"INSERT INTO approve_ticket(token,log_id,session_id,tool_name,params,expire_ts,chat_id,conv_id,tool_call_id)
                    VALUES (@tk,@lid,@sid,@tname,@par,@exp,@cid,@convid,@tcid)";
        var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tk", token);
        cmd.Parameters.AddWithValue("@lid", ticket.LogId);
        cmd.Parameters.AddWithValue("@sid", ticket.SessionId);
        cmd.Parameters.AddWithValue("@tname", ticket.ToolName);
        cmd.Parameters.AddWithValue("@par", System.Text.Json.JsonSerializer.Serialize(ticket.Params));
        cmd.Parameters.AddWithValue("@exp", expireTs);
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.Parameters.AddWithValue("@convid", convId);
        cmd.Parameters.AddWithValue("@tcid", toolCallId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ApproveTicket?> GetTicket(string token)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var conn = GetConnection();
        var sql = @"SELECT log_id,session_id,tool_name,params,chat_id,conv_id,tool_call_id
                    FROM approve_ticket WHERE token=@tk AND expire_ts > @now";
        var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tk", token);
        cmd.Parameters.AddWithValue("@now", now);
        var dr = await cmd.ExecuteReaderAsync();
        ApproveTicket? ret = null;
        if (dr.Read())
        {
            ret = new ApproveTicket
            {
                LogId = dr["log_id"].ToString()!,
                SessionId = dr["session_id"].ToString()!,
                ToolName = dr["tool_name"].ToString()!,
                Params = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(dr["params"].ToString()!)!,
                ChatId = dr["chat_id"].ToString()!,
                ConvId = dr["conv_id"].ToString()!,
                ToolCallId = dr["tool_call_id"].ToString()!
            };
        }
        await dr.CloseAsync();
        return ret;
    }

    public async Task DeleteTicket(string token)
    {
        using var conn = GetConnection();
        var cmd = new SqliteCommand("DELETE FROM approve_ticket WHERE token=@tk", conn);
        cmd.Parameters.AddWithValue("@tk", token);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SaveSnapshot(string logId, object? snapObj, long expireTs)
    {
        if (snapObj == null) return;
        var json = System.Text.Json.JsonSerializer.Serialize(snapObj);
        using var conn = GetConnection();
        var sql = @"INSERT OR REPLACE INTO snapshot_item(log_id,snapshot_json,expire_ts) VALUES (@lid,@json,@exp)";
        var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@lid", logId);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@exp", expireTs);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string?> GetSnapshotJson(string logId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var conn = GetConnection();
        var sql = @"SELECT snapshot_json FROM snapshot_item WHERE log_id=@lid AND expire_ts > @now";
        var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@lid", logId);
        cmd.Parameters.AddWithValue("@now", now);
        var val = await cmd.ExecuteScalarAsync();
        return val == DBNull.Value ? null : val?.ToString();
    }

    private async Task CleanExpireData()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var conn = GetConnection();
        var cmd1 = new SqliteCommand("DELETE FROM approve_ticket WHERE expire_ts < @now", conn);
        cmd1.Parameters.AddWithValue("@now", now);
        await cmd1.ExecuteNonQueryAsync();
        var cmd2 = new SqliteCommand("DELETE FROM snapshot_item WHERE expire_ts < @now", conn);
        cmd2.Parameters.AddWithValue("@now", now);
        await cmd2.ExecuteNonQueryAsync();
    }

    public async Task SaveTicketCozeContext(string approveToken, string chatId, string convId, string toolCallId)
    {
        var ticket = await GetTicket(approveToken);
        if (ticket == null) return;
        using var conn = GetConnection();
        var sql = @"UPDATE approve_ticket SET chat_id=@cid,conv_id=@convid,tool_call_id=@tcid WHERE token=@tk";
        var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@tk", approveToken);
        cmd.Parameters.AddWithValue("@cid", chatId);
        cmd.Parameters.AddWithValue("@convid", convId);
        cmd.Parameters.AddWithValue("@tcid", toolCallId);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    /// <summary>
    /// SSE流式对话 stream=true，返回原始coze sse event流字符串，Controller转发给前端
    /// </summary>
    public async Task StreamChatAsync(string botId, string convId, string userId, string userPrompt, Stream outputStream, CancellationToken ct)
    {
        var token = _cfg.CozeApiToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return;

        var reqBody = new
        {
            bot_id = botId,
            conversation_id = convId,
            user_id = userId,
            stream = true,
            auto_save_history = true,
            additional_messages = new[]
            {
                new { role = "user", content = userPrompt, content_type = "text" }
            }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(reqBody);
        var client = _httpFactory.CreateClient();
        var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.coze.cn/v3/chat");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        // 修复：先 await 获取 Stream，再 CopyToAsync
        using var sseStream = await resp.Content.ReadAsStreamAsync(ct);
        await sseStream.CopyToAsync(outputStream, ct);
    }

    /// <summary>
    /// 【SSE断开之后轮询使用】根据conversation_id查询对话结果，兼容外部http工具调用SSE断流
    /// </summary>
    /// <param name="convId">会话id(conversation_id)</param>
    /// <param name="maxWaitMs">最大等待毫秒</param>
    /// <returns>(isFinish, answerText, isRequireAction)</returns>
    public async Task<(bool isFinish, string answerText, bool isRequireAction)> PollChatResult(string convId, int maxWaitMs = 30000)
    {
        var token = _cfg.CozeApiToken?.Trim();
        var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        while (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime < maxWaitMs)
        {
            await Task.Delay(1200);
            var clientRet = _httpFactory.CreateClient();
            var qs = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://api.coze.cn/v3/chat/list",
                new Dictionary<string, string> { ["conversation_id"] = convId });
            var reqRet = new HttpRequestMessage(HttpMethod.Get, qs);
            reqRet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var retResp = await clientRet.SendAsync(reqRet);
            var retText = await retResp.Content.ReadAsStringAsync();
            var retrieveResp = JsonDocument.Parse(retText);
            if (!retrieveResp.RootElement.TryGetProperty("code", out var rc) || rc.ToString() != "0")
                continue;
            var dataArr = retrieveResp.RootElement.GetProperty("data");
            if (dataArr.ValueKind != System.Text.Json.JsonValueKind.Array || !dataArr.EnumerateArray().Any())
                continue;
            var lastChat = dataArr.EnumerateArray().First();
            var chatId = lastChat.GetProperty("id").ToString()!;
            var status = lastChat.TryGetProperty("status", out var st) ? st.ToString() : null;

            // 遇到工具调用，直接返回标记，交给Coze平台回调/api/tool/call处理
            if (status == "requires_action")
            {
                return (true, "需要调用工具", true);
            }

            if (status == "completed")
            {
                var msgResp = await _httpFactory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Get,
                    Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://api.coze.cn/v3/chat/message/list",
                        new Dictionary<string, string>
                        {
                            ["conversation_id"] = convId,
                            ["chat_id"] = chatId
                        }))
                { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } });
                var msgText = await msgResp.Content.ReadAsStringAsync();
                var msgResult = JsonDocument.Parse(msgText);
                var msgList = msgResult.RootElement.TryGetProperty("data", out var ml2) ? ml2 : default;
                var answer = msgList.EnumerateArray()
                    .Where(m => m.TryGetProperty("role", out var r2) && r2.ToString() == "assistant"
                             && m.TryGetProperty("type", out var t2) && t2.ToString() == "answer")
                    .Select(m => m.TryGetProperty("content", out var c2) ? c2.ToString() : null)
                    .FirstOrDefault() ?? "";
                return (true, answer, false);
            }
            if (status is "failed" or "canceled")
            {
                return (true, $"对话状态:{status}", false);
            }
            // in_progress 继续循环
        }
        return (false, "轮询超时", false);
    }
}

public class SessionStateManager
{
    // 线程安全字典，key = SessionId
    private readonly ConcurrentDictionary<string, SessionState> _store = new();

    /// <summary>获取会话状态，不存在则新建</summary>
    public SessionState GetOrCreate(string sessionId)
    {
        if (_store.TryGetValue(sessionId, out var state))
        {
            return state;
        }
        var newState = new SessionState();
        _store.TryAdd(sessionId, newState);
        return newState;
    }

    /// <summary>更新状态：设置为等待新增订单确认，并缓存待确认订单</summary>
    public void SetAddDraftConfirm(string sessionId, AddDraftOrderPayload payload)
    {
        var state = GetOrCreate(sessionId);
        state.Flow = SessionFlow.AddDraftConfirm;
        state.PendingOrder = payload;
    }

    /// <summary>清空会话，回到空闲（建单成功/取消后调用）</summary>
    public void Clear(string sessionId)
    {
        if (_store.TryGetValue(sessionId, out var state))
        {
            state.Flow = SessionFlow.Idle;
            state.PendingOrder = null;
        }
    }

    /// <summary>直接获取状态，不存在返回null</summary>
    public SessionState? TryGet(string sessionId)
    {
        _store.TryGetValue(sessionId, out var s);
        return s;
    }
}
