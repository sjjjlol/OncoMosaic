using System.Collections.Concurrent;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    checks++;
    Console.WriteLine($"PASS: {name}");
}
void Expect<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, name); return; }
    throw new InvalidOperationException($"FAIL: {name}: expected {typeof(T).Name}");
}

// Observe the public contract: do not test the private dictionary.
var store = new InMemoryAppointmentStore();
Check(store.Get(99) is null, "Missing record returns null");
Expect<ArgumentOutOfRangeException>(() => store.Get(0), "Invalid read ID");
Expect<ArgumentOutOfRangeException>(() => store.Add(0), "Invalid create ID");
Check(store.Add(1), "Create succeeds");
Check(store.Get(1) == new Appointment(1, AppointmentStatus.Pending, 1), "Read initial state");
Check(!store.Add(1), "Duplicate create is rejected");
Check(store.Get(1)?.Version == 1, "Duplicate create keeps existing record");
var firstSnapshot = store.Get(1)!;
Check(store.Transition(1, 1, AppointmentStatus.Confirmed) == UpdateResult.Updated, "Confirm pending record");
Check(firstSnapshot.Status == AppointmentStatus.Pending, "Read snapshot does not change later");
Check(store.Get(1) == new Appointment(1, AppointmentStatus.Confirmed, 2), "Success increments version");
Check(store.Transition(1, 1, AppointmentStatus.Cancelled) == UpdateResult.VersionConflict, "Stale update rejected");
Check(store.Transition(1, 2, AppointmentStatus.Confirmed) == UpdateResult.InvalidTransition, "Repeated confirm rejected");
Check(store.Transition(1, 2, AppointmentStatus.Cancelled) == UpdateResult.InvalidTransition, "Terminal state cannot change");
Check(store.Get(1)?.Version == 2, "Failed update does not increment version");
Check(store.Transition(99, 1, AppointmentStatus.Cancelled) == UpdateResult.NotFound, "Missing update is explicit");
Expect<ArgumentOutOfRangeException>(() => store.Transition(1, 0, AppointmentStatus.Cancelled), "Invalid version");
Expect<ArgumentOutOfRangeException>(() => store.Transition(1, 2, (AppointmentStatus)99), "Invalid enum value");
Check(store.Add(2), "Create another record");
Check(store.Transition(2, 1, AppointmentStatus.Cancelled) == UpdateResult.Updated, "Cancel pending record");
Check(store.Transition(2, 2, AppointmentStatus.Confirmed) == UpdateResult.InvalidTransition, "Cannot confirm cancelled record");

// Gate both real threads after they read version 1; no Thread.Sleep ordering.
store.Add(3);
using (var barrier = new Barrier(2))
{
    UpdateResult Race(AppointmentStatus target)
    {
        var snapshot = store.Get(3)!;
        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Workers did not reach the barrier.");
        return store.Transition(3, snapshot.Version, target);
    }
    var a = Task.Factory.StartNew(() => Race(AppointmentStatus.Confirmed),
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    var b = Task.Factory.StartNew(() => Race(AppointmentStatus.Cancelled),
        CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    var outcomes = await Task.WhenAll(a, b);
    Check(outcomes.Count(x => x == UpdateResult.Updated) == 1, "Concurrent writers: one succeeds");
    Check(outcomes.Count(x => x == UpdateResult.VersionConflict) == 1, "Concurrent writers: one conflicts");
    Check(store.Get(3)?.Version == 2, "Concurrent race commits one version");
}

SupplierClient client = new SpecialSupplierClient();
Check(client.Name() == "special", "Virtual dispatch uses runtime object");
Check(client.Label() == "base-label", "Hidden method uses base reference type");
Check(((SpecialSupplierClient)client).Label() == "special-label", "Derived reference sees hidden member");
Check(NumberRules.Gcd(48, 18) == 6, "GCD normal input");
Check(NumberRules.Gcd(-48, 18) == 6, "GCD negative input");
Check(NumberRules.Gcd(0, 18) == 18, "GCD one zero");
Check(NumberRules.Gcd(0, 0) == 0, "GCD two zeros: chosen contract");
Check(NumberRules.Gcd(int.MinValue, 0) == 2147483648L, "GCD int minimum without overflow");
var items = new[] { (Name: "A", Score: 2), (Name: "B", Score: 1), (Name: "C", Score: 2) };
Check(string.Join(",", items.OrderBy(x => x.Score).Select(x => x.Name)) == "B,A,C", "Stable sort keeps equal-key order");
Console.WriteLine($"ALL {checks} CHECKS PASSED");

public enum AppointmentStatus { Pending, Confirmed, Cancelled }
public enum UpdateResult { Updated, NotFound, VersionConflict, InvalidTransition }

// Immutable read model: callers cannot mutate internal state through Get().
public sealed record Appointment(int Id, AppointmentStatus Status, long Version);

public interface IAppointmentStore
{
    Appointment? Get(int id);
    bool Add(int id);
    UpdateResult Transition(int id, long expectedVersion, AppointmentStatus target);
}

// Exercise only: one process, no persistence, no database transactions.
public sealed class InMemoryAppointmentStore : IAppointmentStore
{
    private readonly ConcurrentDictionary<int, Appointment> _items = new();

    public Appointment? Get(int id)
    {
        ValidateId(id);
        return _items.TryGetValue(id, out var item) ? item : null;
    }

    public bool Add(int id)
    {
        ValidateId(id);
        return _items.TryAdd(id, new Appointment(id, AppointmentStatus.Pending, 1));
    }

    public UpdateResult Transition(int id, long expectedVersion, AppointmentStatus target)
    {
        ValidateId(id);
        if (expectedVersion < 1) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
        if (!_items.TryGetValue(id, out var current)) return UpdateResult.NotFound;
        if (current.Version != expectedVersion) return UpdateResult.VersionConflict;
        if (current.Status != AppointmentStatus.Pending || target == AppointmentStatus.Pending)
            return UpdateResult.InvalidTransition;

        var next = current with { Status = target, Version = checked(current.Version + 1) };
        // Atomic compare-and-swap; checking Version above alone is insufficient.
        return _items.TryUpdate(id, next, current)
            ? UpdateResult.Updated : UpdateResult.VersionConflict;
    }

    private static void ValidateId(int id)
    {
        if (id < 1) throw new ArgumentOutOfRangeException(nameof(id));
    }
}

public class SupplierClient
{
    public virtual string Name() => "base";
    public string Label() => "base-label";
}
public sealed class SpecialSupplierClient : SupplierClient
{
    public override string Name() => "special";
    public new string Label() => "special-label";
}
public static class NumberRules
{
    public static long Gcd(int a, int b)
    {
        long x = Math.Abs((long)a), y = Math.Abs((long)b);
        while (y != 0) (x, y) = (y, x % y);
        return x;
    }
}
