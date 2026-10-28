# 项目固定上下文说明
@docs/requirement.md
@docs/db_schema.md
@docs/dev_spec.md

## AI开发执行规则
1. 所有代码生成、接口编写、SQL、实体类、页面开发，**必须优先参考requirement.md业务需求，db_schema.md表结构**。
2. 本项目使用 sqlite 数据库作为主数据库。
3. 表结构不允许私自新增表、新增字段；需要变更表结构，必须先更新db_schema.md文档。
4. 开发前自动读取两份文档，用户后续指令不需要重复说明文档路径。
5. 注意约束：CodeBase系统类别记录不可删除；关联字段校验在代码层实现，不建立数据库物理外键。
6. 枚举取值严格遵循文档定义，不可自行新增枚举值。
7. **强制遵守 docs/dev_spec.md 全部开发编码规范。**