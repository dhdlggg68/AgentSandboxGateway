using System.Text.Json;
using System.Text.Json.Serialization;
// ===== Coze插件对外入参（POST /api/tool/call）=====
public class ToolRequest
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;
    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;
    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; } = new();

    // ✅扣子插件回调时携带的上下文，用来保存到审批工单
    [JsonPropertyName("chat_id")]
    public string ChatId { get; set; } = string.Empty;
    [JsonPropertyName("conversation_id")]
    public string ConvId { get; set; } = string.Empty;
    [JsonPropertyName("tool_call_id")]
    public string ToolCallId { get; set; } = string.Empty;
}
// ===== Coze插件对外出参 =====
public class ToolResponse
{
    public int Code { get; set; }
    public string Msg { get; set; } = string.Empty;
    public object? Data { get; set; }
    public string? ApproveToken { get; set; }
    public string? LogId { get; set; }
}
// ===== Service内部使用，不对外暴露 =====
public class ToolCallDto
{
    public string SessionId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object> Params { get; set; } = new();
    public string? ApproveToken { get; set; }
}
public class ToolRespDto
{
    public int Code { get; set; }
    public string Msg { get; set; } = string.Empty;
    public object? Data { get; set; }
    public string? ApproveToken { get; set; }
    public string? LogId { get; set; }
}
public class ApproveTicket
{
    public string LogId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object> Params { get; set; } = new();

    // ✅扣子上下文信息
    public string ChatId { get; set; } = string.Empty;
    public string ConvId { get; set; } = string.Empty;
    public string ToolCallId { get; set; } = string.Empty;
}
public class ChatSendDto
{
    public string SessionId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}
public class ToolPolicy
{
    public string Level { get; set; } = string.Empty;
    public bool NeedApprove { get; set; }
    public bool NeedSandbox { get; set; }
}

//订单主表
public class OrderEntity
{
    public long Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int Status { get; set; }
    public string CreateTime { get; set; } = string.Empty;
}
//订单明细
public class OrderItemEntity
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

//工具入参实体，对应智能体传过来payload
public class AddDraftOrderPayload
{
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;
    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }
    [JsonPropertyName("items")]
    public List<OrderItemPayload> Items { get; set; } = new();
}
public class OrderItemPayload
{
    [JsonPropertyName("product_name")]
    public string ProductName { get; set; } = string.Empty;
    [JsonPropertyName("price")]
    public decimal Price { get; set; }
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

public class BatchUpdateOrderPayload
{
    [JsonPropertyName("target_id")]
    public string TargetId { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public int Status { get; set; }
}

public class DeleteOrderPayload
{
    [JsonPropertyName("target_id")]
    public string TargetId { get; set; } = string.Empty;
}

public class QueryOrderPayload
{
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;
}

/// <summary>
/// 会话状态枚举，避免硬编码字符串
/// </summary>
public enum SessionFlow
{
    /// <summary>空闲，无待确认业务</summary>
    Idle,
    /// <summary>新增草稿订单，等待用户确认</summary>
    AddDraftConfirm
}

public class OrderItem
{
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// 会话状态
/// </summary>
public class SessionState
{
    public SessionFlow Flow { get; set; } = SessionFlow.Idle;
    public AddDraftOrderPayload? PendingOrder { get; set; }
}
