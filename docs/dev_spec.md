# 项目开发编码规范 dev_spec.md
> 适用范围：前端 uni-app(App-Plus / JS)、C# 后端服务；数据库SQL；Vue模板；SCSS样式。
> 核心命名总纲：
> 1. 局部变量、方法参数、普通函数：**小驼峰，首字母小写，后续单词首字母大写，不使用下划线、横杠连接**。例：`accountList`、`checkAccountHasBookRecord`
> 2. 全局变量 / 静态常量：**大驼峰，首字母大写，后续单词首字母大写，不使用下划线、横杠连接**。例：`DbName`、`ServerUploadUrl`
> 3. C# 类、接口：大驼峰；C#方法大驼峰；C#局部变量/方法参数：小驼峰。
> 4. 数据库表、字段：全部大驼峰，首字母大写，后续单词首字母大写，不使用下划线、横杠连接，和db_schema.md严格保持一致。
> 5. CSS/class类名：短横线 kebab-case。

## 1 文件命名规范
### 1.1 前端 uni-app
1. vue页面目录&文件：小驼峰，首字母小写，后续单词首字母大写，不使用下划线、横杠连接
    - ✅ `pages/account/account.vue`
    - ❌ `Pages/Account/Account.vue`
2. js工具文件：小驼峰，首字母小写，后续单词首字母大写，不使用下划线、横杠连接
    - ✅ `codebase.js`、`bookkeeping.js`
3. 静态资源static：小写下划线
    - ✅ `account_logo.png`
4. 需求md文档：小写+下划线
    - ✅ `todo_changelog.md`、`overview.md`

### 1.2 C#后端
1. 类文件名：与类名保持一致，大驼峰
    - ✅ `AccountService.cs`、`BookkeepingDto.cs`
2. 控制器：`AccountController.cs`
3. Dto、Model、Service 按模块拆分，禁止一个文件存放多个无关类。

> 禁止中文文件名、空格、特殊字符。

## 2 命名详细规则【JS 前端】
### 2.1 变量
1. 局部变量 / data内响应变量 / 函数入参：**小驼峰（首字母小写）**
```js
// ✅正确
let accountList = [];
let keyword = '';
let currentAccountBalance = 0;

// ❌错误
let AccountList = [];
let account_list = [];
let account-list = [];
```

2.全局变量、模块顶层静态常量：**大驼峰（首字母大写）**
```js
// ✅正确
const DbName = 'MiniDB';
const ServerDownloadUrl = 'http://192.168.31.23:8086/api/upload/download';

// ❌错误
const db_name = 'MiniDB';
const server-download-url = '';
```

### 2.2 函数
1. 普通业务函数、methods 页面方法：**小驼峰，动词开头**
```js
// ✅正确
function checkAccountHasBookRecord(accountId, callback){}
function handleSearch(){}

// ❌错误
function CheckAccountHasBookRecord(){}
function check_account_has_book_record(){}
```

### 2.3 控件 id（template）
小驼峰，语义前缀：`txt`输入框、`btn`按钮、`picker`选择器
```html
✅ <input id="txtAccountKeyword" />
✅ <view id="btnAdd" @click="handleSearch"></view>
```

## 3 命名详细规则【C# 后端】
> JS 与 C# 变量命名思想统一：局部 / 参数小驼峰；全局 / 静态 / 类大驼峰。

1. 局部变量、方法入参：**小驼峰，首字母小写**
```csharp
// ✅正确
var accountList = new List<Account>();
public void DeleteAccount(long accountId)
{
    var hasBookRecord = false;
}

// ❌错误
var AccountList = new List<Account>();
var account_list = new List<Account>();
```

2. 类、接口、服务、Dto、Controller、方法名：**大驼峰 PascalCase**
```csharp
// ✅正确
public class AccountService
{
    public bool CheckAccountHasBookRecord(long accountId)
    {
        return false;
    }
}
public class AccountDto{}
public class AccountController{}
```

3. 类内部静态全局常量：**大驼峰**
```csharp
// ✅正确
public static readonly string DbName = "MiniDB";
public static readonly string ServerUploadUrl = "xxx";
```

4. 接口：以大写字母 `I` 开头，如 `IUserRepository`。

5. 私有字段：camelCase 加下划线前缀，如 `_logger`、`_connectionString`。

6. 常量：全大写下划线分隔（团队常用）或 PascalCase（微软官方），如 `MAX_RETRY_COUNT`。

7. 布尔类型：以 `Is`、`Has`、`Can`、`Should` 开头，如 `IsActive`、`HasPermission`。

8. 异步方法：加 `Async` 后缀，如 `GetDataAsync()`。

9. 泛型类型参数：以 `T` 开头，如 `Repository<TEntity>`。

10. 避免匈牙利命名法：不用 `strName`、`intCount` 这种前缀，IDE 类型检查已足够。

11. 避免缩写：除非是通用缩写（Id、Xml、Ftp、Uri），否则写全称。

12. 注释规范：
‌类、公共方法‌：用XML文档注释（三个斜杠 ///），写清功能、参数和返回值。
‌文件头注释‌：包含功能描述、创建人、创建时间、修改记录。
‌行内注释‌：用 `//` 解释复杂逻辑，不要写显而易见的事, 如 `// 这是一个注释` , //后与文字之间空格。

13. 代码结构
‌缩进‌：统一用4个空格，不用Tab，可在 .editorconfig 里配置。
‌大括号‌：Allman风格，左大括号换行（微软默认）。
‌文件组织‌：按职责分文件夹，如 Models、Services、Repositories、Controllers。
‌成员顺序‌：字段在上，构造方法，公共方法，私有方法在底部。
‌方法长度‌：尽量短，超30行考虑拆分。

C# 禁止：局部变量下划线、横杠连接。

‌C#后台少空行的核心规则是：仅在逻辑边界处保留1个空行，禁止连续2个及以上空行，非必要场景完全不插入空行‌，在保证可读性的前提下最大化压缩代码纵向空间。

✅ 必须保留1个空行的场景
不同方法、属性之间，仅留1个空行分隔，不额外多空。
局部变量声明块和后续业务逻辑之间，仅留1个空行。
方法内不同功能逻辑块之间，仅留1个空行做段落区隔。
using分组（系统命名空间/第三方命名空间/项目内部命名空间）之间，仅留1个空行。

❌ 绝对禁止空行的场景
类、接口、结构体的大括号内部，开头第一行代码前，不插入空行。
单个if/for/while等代码块内部，所有语句之间完全不插空行。
连续的字段声明、连续的属性声明之间，完全不插空行。
using语句块内部、try-catch块内部，语句之间完全不插空行。
方法内只有3行以内的简单逻辑，全程不插任何空行。
大括号{和}的前后，不额外插入空行（空代码块可直接写成{ }）。

🚫 严格禁止的多余空行
任何位置都不允许出现连续2个及以上的空行。
代码行末尾、文件末尾，不保留多余的空白空行。
单行注释、XML文档注释的前后，不额外多插空行。
类和类之间，微软官方要求至少1个空行，少空行规则下也只保留1个，不按旧规范留2行。

## 4 数据库 SQL 命名（不受驼峰约束）
> 表名、字段名：**蛇形命名，全部小写，下划线分隔**，严格和 `db_schema.md` 文档保持一致。
```sql
-- ✅正确
SELECT ID, AccountName, AccountDesc, StatementDate FROM Account;

-- SQL关键字必须大写 SELECT FROM WHERE UPDATE INSERT DELETE
```

## 5 Vue template 模板规范
1. class 类名：kebab-case 短横线命名
```html
✅ <view class="search-bar"></view>
❌ <view class="searchBar"></view>
```

2. v-for 循环必须绑定真实唯一 ID，禁止直接用 index 作为 key
```html
✅ <view v-for="item in accountList" :key="item.ID"></view>
```

3. 模板模块顺序固定：头部搜索栏 → 主体列表 → 悬浮按钮 → 弹窗全部放模板最底部。

4. 大模块之间保留 1 行空行；属性过多每个属性单独一行。

5. template 内禁止写业务逻辑，业务全部放到 methods。

## 6 SCSS / CSS 样式规范
1. class 全部 kebab-case 短横线命名，`.search-bar`、`.card-item`。
2. scss 嵌套层级：**最大不超过 4 层**，禁止无限深层嵌套。
3. 颜色：优先使用需求文档定义的全局色值，禁止魔法数字到处写颜色码；可以写 scss 变量。
4. rpx 单位，不要混用 px。
5. 每个大样式块之间保留一行空行；不要全部挤在一起。
6. 禁止写全局污染样式，尽量页面内隔离；不要使用`*`全局通配符。
7. 样式书写顺序：布局 display → 宽高 → margin/padding → 背景 → 文字 → 圆角阴影。
```scss
.search-bar {
  display: flex;
  gap: 20rpx;
  padding: 20rpx;
  background: #4A90D9;

  .search-input-wrap {
    flex: 1;
  }
}
```

## 7 JS 代码排版、空行、大括号规范
1. import 导入块结束后**保留 1 个空行**，再写业务代码。
2. 变量定义：连续变量定义之间不空行；最后一组变量定义之后空 1 行。代码与下一行注释之间空 1 行；如果方法内第一行是注释，则注释前不空行。
3. methods{}：**每个方法结束后与下一个方法之间保留 1 行空行**。
4. if /for/while 大括号 `{` 和语句同一行开启；`}` 单独占一行。
5. 链式调用：过长换行对齐。
6. 禁止连续 2 行及以上空行；禁止行末尾多余空格。
7. 废弃代码直接删除，**不要大段注释掉旧代码**。

```js
// ✅ 示例 JS 模板
import { getAccountList } from '../../utils/account.js'

const DbName = 'MiniDB';

export default {
  data() {
    return {
      keyword: '',
      accountList: []
    }
  },

  onLoad() {

  },

  methods: {
    handleSearch() {

    },

    clearSearch() {

    }
  }
}
```

## 8 注释规范
1. 业务函数必须写 JSDoc 注释，`/** */`，写清入参、callback 含义。
2. 单行注释使用 `// `，注释后加空格。
3. js 文件头部，统一写文件说明：程序说明、建立者、建立日期、修改记录。
4. 代码与下一行注释之间空 1 行；如果方法内第一行是注释，则注释前不空行。
```js
/**
 * 校验账户是否存在关联账务流水
 * @param {number} accountId 账户ID
 * @param {function} callback 返回true存在流水，false无流水
 */
function checkAccountHasBookRecord(accountId, callback){}
```

## 9 JS 业务强制约束
1. uni.showLoading() 打开，无论成功失败，必须执行 uni.hideLoading() 成对关闭。
2. 现有项目沿用 callback 回调风格，不要擅自全部改造 async/await。

## 10 C# 后端业务约束
1. Dto 与数据库实体分开，禁止直接返回数据库实体对外输出。
2. 接口入参做非空、范围校验；统一返回包装结果对象。
3. 数据库操作异常捕获，日志记录；不把原始异常堆栈直接返回前端。

## 11 TRAE AI 开发强制约束
> 生成所有前端 JS/Vue、C# 后端代码，必须遵守本规范。

1. 变量、函数严格区分：局部 / 参数小驼峰；全局 / 静态常量大驼峰；禁止下划线、横杠拼接变量名。
2. 文件命名、空行、大括号、注释格式严格执行；连续变量定义之间不空行，最后一组变量定义后空 1 行；代码与下一行注释之间空 1 行；方法内第一行是注释时注释前不空行。
3. JS：methods 方法小驼峰动词开头；全局常量大驼峰。
4. C#：局部变量入参小驼峰；类、方法、Dto、Controller 大驼峰。
5. SQL 关键字大写；表名字段蛇形小写下划线分隔。
6. scss class 短横线命名，嵌套层级≤4。
7. 禁止遗留大段注释废弃代码；禁止多余连续空行；禁止行尾空格。
