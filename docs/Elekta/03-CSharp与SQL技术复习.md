# Elekta｜C#、SQL与工程基础

以Java经验为基础复习。以下是JD导向练习，不是已确认面试题或MOSAIQ技术架构。框架版本应以团队为准。更完整内容见已有C#/.NET速学（外部个人笔记，未收录于本仓库）。

## 1. Java转C#：先掌握这些差异

| 主题 | 30秒回答 | 常见误区 |
|---|---|---|
| 值类型/引用类型 | struct赋值通常复制值，class变量赋值复制引用；结构体中的引用成员仍指向同一对象 | 不用“值类型全在栈上”解释 |
| GC与资源 | GC管理托管内存；文件、连接等资源生命周期用Dispose/using管理 | GC不等于及时关闭所有资源 |
| async/await | Task表示异步操作；await等待未完成操作时不阻塞当前线程，并不自动创建新线程 | Task不等于Thread，异步不等于并行 |
| LINQ | 可用查询表达式/扩展方法描述筛选与投影；很多操作延迟到枚举时执行 | 重复枚举可能重复工作，ToList会物化 |
| DI | 构造器声明依赖，便于替换和测试；Transient每次解析，Scoped每作用域，Singleton容器内单例 | 不让单例长期捕获请求作用域对象 |
| Nullable | 可空引用类型主要提供编译期分析；外部输入仍需运行时校验 | 开启注解不保证运行时绝不出现null |

依据：[值类型](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/value-types)、[Dispose](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose)、[异步](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/)、[LINQ延迟执行](https://learn.microsoft.com/en-us/dotnet/standard/linq/deferred-execution-lazy-evaluation)、[DI指导](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines)。

### 异步追问

**为什么少用.Result/.Wait()？**会阻塞线程；带同步上下文的环境可能发生死锁，服务端也可能增加线程池压力。不能说任何环境必然死锁。

**取消请求会不会回滚远端动作？**不会。取消是协作式信号；请求发出后远端仍可能成功，需查询实际业务状态。

**Task.WhenAll适合什么？**独立异步操作；还要控制并发、时间预算和下游容量。不能对成千上万条数据无限发请求。

### 最小C#阅读题

```csharp
var values = new List<int> { 1, 2, 3 };
var query = values.Where(x => x > 1);
values.Add(4);
Console.WriteLine(string.Join(",", query)); // 2,3,4
var snapshot = query.ToList();
values.Add(5);
Console.WriteLine(string.Join(",", snapshot)); // 2,3,4
```

解释延迟执行与物化；不要把集合遍历的示例当成所有数据库查询提供程序都具备相同实现。

## 2. OOP与设计模式

**封装的价值？**把有效状态和变化规则放到模块内部，而不是只给每个字段写getter/setter。例如“已取消预约不能再次确认”应由统一状态转换入口约束，不能只靠按钮隐藏。

**接口和抽象类怎么选？**接口表达行为契约，利于替换；抽象类共享状态或流程。优先看变化维度、所有权和测试需要，不机械地为每个类建接口。

**SOLID怎么举例？**用EasyAccess讲职责分离、供应商适配、稳定骨架与变化规则；能解释新增服务影响哪些模块，比逐字背缩写更有说服力。

**怎么写可测试代码？**把纯规则与数据库/网络/时钟等外部依赖分开，测试规则边界；对真实数据库、协议行为仍做集成测试，mock不能证明真实兼容。

## 3. SQL手写：虚构预约表

示例仅用于练习，字段不代表MOSAIQ。采用SQL Server语法。

```sql
-- Appointment(Id, PatientId, StartAt, Status, Version)
-- 1. 时间范围内每名患者已确认预约数；半开区间避免日期末尾精度问题
SELECT PatientId, COUNT(*) AS ConfirmedCount
FROM Appointment
WHERE Status = 'Confirmed' AND StartAt >= @from AND StartAt < @to
GROUP BY PatientId
HAVING COUNT(*) >= 2;

-- 2. 每名患者最近一条预约；Id用于解决时间相同的排序歧义
WITH Ranked AS (
  SELECT Id, PatientId, StartAt, Status,
         ROW_NUMBER() OVER (
           PARTITION BY PatientId ORDER BY StartAt DESC, Id DESC
         ) AS rn
  FROM Appointment
)
SELECT Id, PatientId, StartAt, Status FROM Ranked WHERE rn = 1;

-- 3. 乐观并发：Version为应用维护的整数版本，不是rowversion类型
UPDATE Appointment
SET Status = @newStatus, Version = Version + 1
WHERE Id = @id AND Version = @expectedVersion AND Status = 'Pending';
-- 影响1行表示本次状态转换成功；0行需处理不存在/版本冲突/状态不合法。
```

**追问LEFT JOIN：**右表过滤条件写WHERE可能滤掉没有匹配行的左表记录；若需保留这些记录，依据语义把条件放ON。先说期望结果，再决定写法。

**分页：**必须稳定排序；SQL Server可用ORDER BY配合OFFSET/FETCH。深分页可考虑以(StartAt, Id)为游标，但分页期间数据变化仍需定义一致性。MySQL的LIMIT语法不能直接照搬。

## 4. 索引、事务与并发

**查询慢怎么查？**先确认真实慢请求、参数和结果集规模，再看执行计划、逻辑读、阻塞、扫描及排序。提出候选索引后测量收益与写入开销，不一看到慢就加索引。

**复合索引怎么设计？**结合等值条件、范围条件、排序和选择性。候选(Status, StartAt, Id)只是一种练习方案，不能在不知道分布与查询负载时说最优。

**如何防止丢失更新？**版本条件更新并检查影响行数；业务关联多行时明确事务边界及隔离要求。隔离级别解决可见性/并发问题，不替代业务校验。

**死锁怎么办？**抓死锁证据，定位访问顺序、事务时长和索引，尽量统一顺序、缩短事务。只对可安全重试的完整事务做有界重试，不能简单重放已发生的外部动作。

**rowversion是什么？**SQL Server自动生成的8字节版本标记，可用于检测行变化，不是日期时间，也不是应用手动加一的业务版本。[微软rowversion文档](https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql?view=sql-server-ver17)。锁与行版本细节以[事务指南](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide?view=sql-server-2017)及实际配置为准，不混用MySQL默认隔离级别。

## 5. API、Git、测试和CI

- **API契约：**字段、单位、时区、错误码、分页和兼容策略；客户端重试前判断操作是否幂等。
- **Git：**用小提交解释单一意图；merge保留分支合并关系，rebase重放提交。共享分支避免未经协调改写历史；已发布变更通常用revert形成反向提交，数据恢复另行处理。
- **Code review：**需求符合、边界与错误路径、并发与资源、敏感日志、测试及维护成本；指出具体失败场景，不只评价风格。
- **测试分层：**规则单测、数据库/接口集成测试、关键用户流程测试；验证空值、边界、并发、重复、超时、升级兼容。
- **CI示例：**构建→静态检查→单测→集成测试→生成可追溯制品与结果。是否自动部署由产品发布流程决定，不能把CI通过等同于临床发布批准。
- **Scrum：**能说明如何拆任务、澄清验收条件、暴露阻塞、参与评审和回顾；不将日常站会当成敏捷全部内容。

## 6. 60–90分钟实践任务（尚未执行）

用C#实现一个内存预约状态类，仅支持Pending→Confirmed或Cancelled；非法转换返回明确错误。为重复确认、取消后确认、缺失ID写测试；随后在SQL Server练习上述版本更新。用两个客户端读同一版本，验证只有一个更新成功。

验收：能运行、解释失败路径、说清内存演示与数据库并发的差异。环境未准备好时先做纯规则练习。只有实际完成后才在面试中说“我写过这个Demo”。
