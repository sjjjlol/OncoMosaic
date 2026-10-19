import java.util.List;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CyclicBarrier;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;

/** Standalone Java 17+ interview exercise; no database or external libraries. */
public class InterviewPractice {
    private static int checks;

    public static void main(String[] args) throws Exception {
        AppointmentStore store = new InMemoryAppointmentStore();
        check(store.get(99) == null, "Missing record returns null");
        expectIllegalArgument(() -> store.get(0), "Invalid read ID");
        expectIllegalArgument(() -> store.add(0), "Invalid create ID");
        check(store.add(1), "Create succeeds");
        check(store.get(1).equals(new Appointment(1, Status.PENDING, 1)), "Read initial state");
        check(!store.add(1), "Duplicate create is rejected");
        check(store.get(1).version() == 1, "Duplicate create keeps existing record");
        Appointment firstSnapshot = store.get(1);
        check(store.transition(1, 1, Status.CONFIRMED) == UpdateResult.UPDATED, "Confirm pending record");
        check(firstSnapshot.status() == Status.PENDING, "Read snapshot does not change later");
        check(store.get(1).equals(new Appointment(1, Status.CONFIRMED, 2)), "Success increments version");
        check(store.transition(1, 1, Status.CANCELLED) == UpdateResult.VERSION_CONFLICT, "Stale update rejected");
        check(store.transition(1, 2, Status.CONFIRMED) == UpdateResult.INVALID_TRANSITION, "Repeated confirm rejected");
        check(store.transition(1, 2, Status.CANCELLED) == UpdateResult.INVALID_TRANSITION, "Terminal state cannot change");
        check(store.get(1).version() == 2, "Failed update does not increment version");
        check(store.transition(99, 1, Status.CANCELLED) == UpdateResult.NOT_FOUND, "Missing update is explicit");
        expectIllegalArgument(() -> store.transition(1, 0, Status.CANCELLED), "Invalid version");
        expectIllegalArgument(() -> store.transition(1, 2, null), "Null target rejected");
        check(store.add(2), "Create another record");
        check(store.transition(2, 1, Status.CANCELLED) == UpdateResult.UPDATED, "Cancel pending record");
        check(store.transition(2, 2, Status.CONFIRMED) == UpdateResult.INVALID_TRANSITION, "Cannot confirm cancelled record");

        store.add(3);
        var barrier = new CyclicBarrier(2);
        var pool = Executors.newFixedThreadPool(2);
        try {
            var a = pool.submit(() -> race(store, barrier, Status.CONFIRMED));
            var b = pool.submit(() -> race(store, barrier, Status.CANCELLED));
            var outcomes = List.of(a.get(15, TimeUnit.SECONDS), b.get(15, TimeUnit.SECONDS));
            check(outcomes.stream().filter(x -> x == UpdateResult.UPDATED).count() == 1,
                    "Concurrent writers: one succeeds");
            check(outcomes.stream().filter(x -> x == UpdateResult.VERSION_CONFLICT).count() == 1,
                    "Concurrent writers: one conflicts");
            check(store.get(3).version() == 2, "Concurrent race commits one version");
        } finally {
            pool.shutdownNow();
            if (!pool.awaitTermination(5, TimeUnit.SECONDS))
                throw new IllegalStateException("Worker shutdown timed out");
        }

        SupplierClient client = new SpecialSupplierClient();
        check(client.name().equals("special"), "Instance method dispatch uses runtime object");
        check(SupplierClient.label().equals("base-label"), "Base static member");
        check(SpecialSupplierClient.label().equals("special-label"), "Derived static member hides base member");
        check(gcd(48, 18) == 6, "GCD normal input");
        check(gcd(-48, 18) == 6, "GCD negative input");
        check(gcd(0, 18) == 18, "GCD one zero");
        check(gcd(0, 0) == 0, "GCD two zeros: chosen contract");
        check(gcd(Integer.MIN_VALUE, 0) == 2147483648L, "GCD int minimum without overflow");
        var items = List.of(new Score("A", 2), new Score("B", 1), new Score("C", 2));
        var names = items.stream().sorted(java.util.Comparator.comparingInt(Score::value))
                .map(Score::name).toList();
        check(names.equals(List.of("B", "A", "C")), "Ordered stream stable sort keeps equal-key order");
        System.out.println("ALL " + checks + " CHECKS PASSED");
    }

    private static UpdateResult race(AppointmentStore store, CyclicBarrier barrier, Status target)
            throws Exception {
        var snapshot = store.get(3);
        barrier.await(10, TimeUnit.SECONDS);
        return store.transition(3, snapshot.version(), target);
    }

    private static void check(boolean condition, String name) {
        if (!condition) throw new AssertionError("FAIL: " + name);
        checks++;
        System.out.println("PASS: " + name);
    }

    private static void expectIllegalArgument(Runnable action, String name) {
        try { action.run(); }
        catch (IllegalArgumentException expected) { check(true, name); return; }
        throw new AssertionError("FAIL: " + name + ": expected IllegalArgumentException");
    }

    enum Status { PENDING, CONFIRMED, CANCELLED }
    enum UpdateResult { UPDATED, NOT_FOUND, VERSION_CONFLICT, INVALID_TRANSITION }
    record Appointment(int id, Status status, long version) { }
    record Score(String name, int value) { }

    interface AppointmentStore {
        Appointment get(int id);
        boolean add(int id);
        UpdateResult transition(int id, long expectedVersion, Status target);
    }

    static final class InMemoryAppointmentStore implements AppointmentStore {
        private final ConcurrentHashMap<Integer, Appointment> items = new ConcurrentHashMap<>();

        public Appointment get(int id) {
            validateId(id);
            return items.get(id);
        }

        public boolean add(int id) {
            validateId(id);
            return items.putIfAbsent(id, new Appointment(id, Status.PENDING, 1)) == null;
        }

        public UpdateResult transition(int id, long expectedVersion, Status target) {
            validateId(id);
            if (expectedVersion < 1) throw new IllegalArgumentException("Version must be positive");
            if (target == null) throw new IllegalArgumentException("Target is required");
            Appointment current = items.get(id);
            if (current == null) return UpdateResult.NOT_FOUND;
            if (current.version() != expectedVersion) return UpdateResult.VERSION_CONFLICT;
            if (current.status() != Status.PENDING || target == Status.PENDING)
                return UpdateResult.INVALID_TRANSITION;
            Appointment next = new Appointment(id, target, Math.addExact(current.version(), 1));
            // Atomic comparison and replacement; a separate check and put is unsafe.
            return items.replace(id, current, next) ? UpdateResult.UPDATED : UpdateResult.VERSION_CONFLICT;
        }

        private static void validateId(int id) {
            if (id < 1) throw new IllegalArgumentException("ID must be positive");
        }
    }

    static class SupplierClient {
        public String name() { return "base"; }
        public static String label() { return "base-label"; }
    }
    static final class SpecialSupplierClient extends SupplierClient {
        @Override public String name() { return "special"; }
        public static String label() { return "special-label"; }
    }

    static long gcd(int a, int b) {
        long x = Math.abs((long) a), y = Math.abs((long) b);
        while (y != 0) {
            long remainder = x % y;
            x = y;
            y = remainder;
        }
        return x;
    }
}
