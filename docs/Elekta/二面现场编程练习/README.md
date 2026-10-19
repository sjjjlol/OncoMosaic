# 二面现场编程练习：Java主练，C#对照

用户已确认现场可以使用Java。此目录是独立学习练习，不修改OncoMosaic业务实现，也不启动ClinicFlow。两种语言实现相同的状态/版本契约，均无外部包。

## 1. Java主练

需要Java 17或以上（使用record）；本机运行环境为Java24.0.1。在仓库根目录运行：

```bash
java docs/Elekta/二面现场编程练习/InterviewPractice.java
```

参考源码：[InterviewPractice.java](InterviewPractice.java)。成功输出每条PASS，最后`ALL 32 CHECKS PASSED`。失败以非零状态退出，不依赖`-ea`开启Java assert。

## 2. C#对照

需要.NET 10 SDK；本机核查为10.0.401：

```bash
dotnet run --project docs/Elekta/二面现场编程练习/InterviewPractice.csproj
```

参考源码：[Program.cs](Program.cs)。成功同样为32项检查通过。C#的virtual/override/new与Java的实例重写/静态隐藏例子并不完全相同，要理解各自规则。

2026-10-10两个版本均已实际运行，退出码0，分别32项全部通过；包含有屏障控制的两线程竞争检查。Java代码另以`javac --release 17`验证语言/API兼容。这些是轻量控制台行为检查，不是JUnit或xUnit测试工程。

## 3. 怎么练

1. 先读[任务与测试契约](../12-现场编程与数据库测试.md)，不要先看参考。
2. 给自己35分钟，用Java从类型、契约和正常流程开始独立写。
3. 开口解释：`The version check and the update must be one atomic operation.`
4. 对照参考，理解失败场景及最终状态断言。
5. 再加一题：若允许Confirmed→Cancelled，需要修改哪些契约、测试和并发语义？

Java关键点：不可变record、ConcurrentHashMap.putIfAbsent与replace(key,old,new)、CyclicBarrier、ExecutorService清理、明确业务结果枚举。若现场要求旧Java版本，可将record改为不可变类，提供正确equals/hashCode；不要临时换整个业务设计。

## 4. 包含什么

- 不可变Appointment、状态机、缺失与版本冲突结果。
- 原子比较更新；两执行线程在都读完版本后，通过屏障同步开始提交，不用Sleep猜交错。
- OOP运行时派发与隐藏成员的语言差异。
- GCD包含负数、零和int最小值。
- 稳定排序例子：Java有序Stream.sorted；C# LINQ OrderBy。

## 5. 不证明什么

没有真实数据库、持久化、事务回滚、审计、幂等请求记录、租约、远端副作用或多进程协调。真实数据库测试矩阵见12；预约从零设计见13。当前状态机把确认和取消设为终态，仅用于练习。
