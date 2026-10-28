using Microsoft.Data.Sqlite;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateBuilder(args);
// IIS配置
builder.Services.Configure<IISServerOptions>(opt =>
{
    opt.AutomaticAuthentication = false;
});
builder.WebHost.UseSetting("allowedHosts", "*");
builder.Services.AddCors(o =>
{
    o.AddPolicy("AllowAll", p =>
    {
        p.AllowAnyOrigin()
         .AllowAnyMethod()
         .AllowAnyHeader();
    });
});
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
});
builder.Services.AddSingleton<SessionStateManager>();
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.WriteIndented = false;
        opt.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllersWithViews()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddHttpClient();
builder.Services.AddScoped<AgentSandboxService>();
builder.Services.AddSingleton<AppConfig>(sp => new AppConfig
{
    CozeBotId = "7701224392265138191",
    CozeApiToken = "pat_FngCdrjCHGu4WueNmM8XFD2W5b6Y3BHhIiFFz53OhCaoh5KiFqISo8Kkcv5KLzP3",
    ProdApiBase = "https://api.biz-demo.com",
    SandboxApiBase = "https://sandbox.biz-demo.com",
    SqliteLogPath = Path.Combine(Directory.GetCurrentDirectory(), "agent_log.db")
});
var app = builder.Build();
app.UseCors("AllowAll");
app.UseSession();

// 初始化全部数据表
var cfg = app.Services.GetRequiredService<AppConfig>();
using var conn = new SqliteConnection($"Data Source={cfg.SqliteLogPath}");
conn.Open();
// 原有日志表
var sqlLog = @"
CREATE TABLE IF NOT EXISTS tool_log (
    log_id TEXT PRIMARY KEY,
    tool_name TEXT,
    risk_level TEXT,
    session_id TEXT,
    input_params TEXT,
    snapshot TEXT,
    sandbox_result TEXT,
    prod_result TEXT,
    approve_status TEXT,
    approve_user TEXT,
    create_ts INTEGER,
    create_time TEXT DEFAULT (datetime('now', 'localtime')),
    is_rollback INTEGER DEFAULT 0,
    rollback_ts INTEGER
);";
_ = new SqliteCommand(sqlLog, conn).ExecuteNonQuery();

// ✅扩展审批工单表，增加扣子会话字段 chat_id,conv_id,tool_call_id
var sqlTicket = @"
CREATE TABLE IF NOT EXISTS approve_ticket (
    token TEXT PRIMARY KEY,
    log_id TEXT,
    session_id TEXT,
    tool_name TEXT,
    params TEXT,
    expire_ts INTEGER,
    chat_id TEXT,
    conv_id TEXT,
    tool_call_id TEXT,
    create_time TEXT DEFAULT (datetime('now', 'localtime'))
);";
_ = new SqliteCommand(sqlTicket, conn).ExecuteNonQuery();
// 兼容旧库，如果旧表没有新增字段执行alter
try
{
    new SqliteCommand("ALTER TABLE approve_ticket ADD COLUMN chat_id TEXT", conn).ExecuteNonQuery();
}
catch { /*字段已存在忽略*/ }
try
{
    new SqliteCommand("ALTER TABLE approve_ticket ADD COLUMN conv_id TEXT", conn).ExecuteNonQuery();
}
catch { }
try
{
    new SqliteCommand("ALTER TABLE approve_ticket ADD COLUMN tool_call_id TEXT", conn).ExecuteNonQuery();
}
catch { }

// 快照表（回滚）
var sqlSnapshot = @"
CREATE TABLE IF NOT EXISTS snapshot_item (
    log_id TEXT PRIMARY KEY,
    snapshot_json TEXT,
    expire_ts INTEGER,
    create_time TEXT DEFAULT (datetime('now', 'localtime'))
);";
_ = new SqliteCommand(sqlSnapshot, conn).ExecuteNonQuery();

// 订单主表
var sqlOrders = @"
CREATE TABLE IF NOT EXISTS orders (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    order_id TEXT NOT NULL,
    total_amount REAL NOT NULL DEFAULT 0,
    status INTEGER DEFAULT 0,
    create_time TEXT DEFAULT (datetime('now', 'localtime')),
    UNIQUE (order_id ASC)
);";
_ = new SqliteCommand(sqlOrders, conn).ExecuteNonQuery();

// 订单明细表
var sqlOrderItem = @"
CREATE TABLE IF NOT EXISTS order_item (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    order_id INTEGER NOT NULL,
    product_name TEXT NOT NULL,
    price REAL NOT NULL,
    quantity INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE CASCADE ON UPDATE NO ACTION
);";
_ = new SqliteCommand(sqlOrderItem, conn).ExecuteNonQuery();

conn.Close();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
public class AppConfig
{
    public string CozeBotId { get; set; } = string.Empty;
    public string CozeApiToken { get; set; } = string.Empty;
    public string ProdApiBase { get; set; } = string.Empty;
    public string SandboxApiBase { get; set; } = string.Empty;
    public string SqliteLogPath { get; set; } = string.Empty;
}
