# 数据库表结构文档
数据库名：MiniDB.db
数据库类型：SQLite

## 表1：orders 订单表
表注释：订单主表

| 字段名 | 数据类型 | 长度 | 是否非空 | 默认值 | 注释 |
|--------|----------|------|----------|--------|------|
| id | INTEGER | - | YES | AUTOINCREMENT | 主键 |
| order_id | TEXT | - | YES |  | 订单编号（唯一） |
| total_amount | REAL | - | YES | 0 | 订单总金额 |
| status | INTEGER | - | NO | 0 | 订单状态 |
| create_time | TEXT | - | NO | datetime('now', 'localtime') | 创建时间 |

索引：
1. PRIMARY：id 主键索引
2. UNIQUE：order_id 唯一索引

## 表2：order_item 订单明细表
表注释：订单明细表

| 字段名 | 数据类型 | 长度 | 是否非空 | 默认值 | 注释 |
|--------|----------|------|----------|--------|------|
| id | INTEGER | - | YES | AUTOINCREMENT | 主键 |
| order_id | INTEGER | - | YES |  | 订单主键ID，关联orders.id |
| product_name | TEXT | - | YES |  | 商品名称 |
| price | REAL | - | YES |  | 单价 |
| quantity | INTEGER | - | NO | 1 | 数量 |

索引：
1. PRIMARY：id 主键索引
2. FOREIGN KEY：order_id 关联 orders.id（ON DELETE CASCADE）

## 表3：tool_log 工具调用日志表
表注释：记录每次工具调用的入参、快照、沙箱与正式执行结果

| 字段名 | 数据类型 | 长度 | 是否非空 | 默认值 | 注释 |
|--------|----------|------|----------|--------|------|
| log_id | TEXT | - | YES |  | 主键，唯一ID |
| tool_name | TEXT | - | NO |  | 工具名称 |
| risk_level | TEXT | - | NO |  | 风险等级（L0/L1/L2） |
| session_id | TEXT | - | NO |  | 会话ID |
| input_params | TEXT | - | NO |  | 入参JSON |
| snapshot | TEXT | - | NO | NULL | 操作前快照JSON |
| sandbox_result | TEXT | - | NO | NULL | 沙箱预演结果JSON |
| prod_result | TEXT | - | NO | NULL | 正式执行结果JSON |
| approve_status | TEXT | - | NO |  | 审批状态 |
| approve_user | TEXT | - | NO | NULL | 审批人 |
| create_ts | INTEGER | - | NO |  | 创建时间戳（秒） |
| create_time | TEXT | - | NO | datetime('now', 'localtime') | 新增时间 |
| is_rollback | INTEGER | - | NO | 0 | 是否已回滚 |
| rollback_ts | INTEGER | - | NO | NULL | 回滚时间戳（秒） |

## 表4：approve_ticket 审批工单表
表注释：待人工审批的高危操作工单

| 字段名 | 数据类型 | 长度 | 是否非空 | 默认值 | 注释 |
|--------|----------|------|----------|--------|------|
| token | TEXT | - | YES |  | 主键，审批令牌 |
| log_id | TEXT | - | NO |  | 关联tool_log.log_id |
| session_id | TEXT | - | NO |  | 会话ID |
| tool_name | TEXT | - | NO |  | 工具名称 |
| params | TEXT | - | NO |  | 操作参数JSON |
| expire_ts | INTEGER | - | NO |  | 过期时间戳（秒） |
| chat_id | TEXT | - | NO | NULL | 扣子对话ID |
| conv_id | TEXT | - | NO | NULL | 扣子会话ID |
| tool_call_id | TEXT | - | NO | NULL | 扣子工具调用ID |
| create_time | TEXT | - | NO | datetime('now', 'localtime') | 新增时间 |

## 表5：snapshot_item 回滚快照表
表注释：记录修改/删除前的原始数据，用于回滚

| 字段名 | 数据类型 | 长度 | 是否非空 | 默认值 | 注释 |
|--------|----------|------|----------|--------|------|
| log_id | TEXT | - | YES |  | 主键，关联tool_log.log_id |
| snapshot_json | TEXT | - | NO |  | 快照JSON |
| expire_ts | INTEGER | - | NO |  | 过期时间戳（秒） |
| create_time | TEXT | - | NO | datetime('now', 'localtime') | 新增时间 |
