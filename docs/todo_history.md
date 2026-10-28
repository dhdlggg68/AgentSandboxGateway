# 待优化点与迭代变更记录
> 说明：
> - ✅ 已完成：迭代开发测试完毕，归档保留，不再执行
> - 📋 待执行：新的迭代任务，TRAE只处理标记📋的内容

## ✅ 迭代2026‑09‑13：账户、代码档页面搜索栏与删除校验
> 状态：全部开发完成，测试通过，归档

### 公共约束
1. 搜索栏UI样式、间距、圆角、图标大小，两个页面保持视觉完全一致。
2. 所有校验逻辑下沉到utils js模块，vue页面只调用返回结果，不写原始SQL语句。
3. 校验查询数据库时需要增加加载提示，增加异常捕获，数据库报错给出toast提示。
4. 收到`dbDownloadSuccess`全局事件，同时重置搜索关键字，刷新完整列表。

## ✅ 已完成：迭代开发测试完毕，归档保留，不再执行
> 状态：待开发，TRAE读取后只执行本块内容
1. 记账功能 pages/bookkeeping/bookkeeping
    -页面更入的是收入还是支出，这个栏位的标题改为类型
    - 收支类型的下拉选单，根据当前页面类型是收入还是支出取代码档的Category是IncomeType收入类型，或ExpenseType支出类型。
2. 提取共用样式到 common/common.scss，包含颜色变量、字体大小、间距、圆角、共用组件类（搜索栏、FAB、空状态、底部选择器、表单项、弹窗按钮、列表容器）
3. 统一三个新增页面（account.vue / bookkeeping.vue / bookkeepingList.vue）样式，全部改为 scss 变量引用 common/common.scss，消除风格差异
4. 所有新增页面（含 codebase.vue）样式风格统一，均使用 common/common.scss 全局 scss 变量
5. 所有页面的下拉选单，改为可输入并自动匹配选项。
6. 记账功能 pages/bookkeeping/bookkeeping及pages/bookkeeping/bookkeepingList：收支类型按页面类型（收入取IncomeType，支出取ExpenseType）加载
7. pages/bookkeeping/bookkeepingList 搜索区底色还原暖橘色（rgba(255, 248, 242, 0.95)），搜索区按钮颜色与底色搭配（橙色图标按钮）
8. 记账功能清单 pages/bookkeeping/bookkeepingList：清单卡片增加收入/支出标签
9. 新建 pages/statistics/statistics 统计报表页面，使用 qiun-data-charts 组件展示收支柱状图和账户类型饼图，支持按日期/账户筛选及类型切换