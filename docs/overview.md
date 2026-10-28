# 项目总览与全局公共规则
项目名称：ERP管理系统
版本：V1.0
更新日期：2026‑09‑28

## 1. 项目概述
本系统为 DevExpressv16.1 + MVC5 + uni‑app + SQL server 本地ERP管理的综合性管理系统。
uni‑app后端服务WebApi，所有数据保存在SQL server。

核心数据表：
1. CodeBase：基础代码字典表，存储分类字典数据（账户类型、收支类型等，Category='Z'为分类定义）
2. Account：账户主表，维护各个账户基础信息、初始余额、账单日还款日
3. AccountData：账务流水表，每一笔收入/支出记录

> 技术栈：uni‑app(App‑Plus)；页面全部vue组件。

## 2.1 加载与提示规范
1. 加载：`uni.showLoading`，无论成功失败必须执行`uni.hideLoading()`
2. 成功提示：`icon:"success"`；业务异常、表单校验：`icon:"none"`
3. 删除操作统一弹出确认模态框，确认后执行删除。

> 页面禁止手写大量SQL，全部调用WebApi的业务函数。

