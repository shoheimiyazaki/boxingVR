namespace Aigis.Physics;

/// <summary>
/// Caps how many contact pairs one dynamic body may take part in per step, so no constraint ever needs more batches
/// than the solver has — the guarantee that keeps the world out of BepuPhysics' fallback batch.
///
/// <para><b>Why the fallback batch must never be used.</b> The solver puts constraints in batches that each hold at
/// most one constraint per dynamic body; past <see cref="PhysicsWorld.SolverBatchLimit"/> batches the rest go to one
/// sequential "fallback" batch. BepuPhysics 2.4.0 throws a NullReferenceException inside <c>Simulation.Timestep</c>
/// when a fallback constraint with a KINEMATIC body in it is removed after the fallback's last dynamic body has left
/// (bepu/bepuphysics2#425, open). It gets there two ways: a dynamic body touched by that many kinematics, or a body
/// made kinematic (<see cref="PhysicsWorld.MakeKinematic"/>, a carried patient) while its contact sits in the
/// fallback. The exception leaves the simulation broken — its built-in profiler still holds the step it never ended,
/// so every later step throws "same key has already been added" — and the world never moves again.</para>
///
/// <para><b>The bound.</b> A new constraint goes in the first batch neither of its bodies is in yet, so it lands at
/// index at most deg(A) + deg(B) − 2, where deg counts each body's constraints. Admitting at most
/// <see cref="PhysicsWorld.ContactPairsPerBody"/> non-static pairs per body per step keeps a body's contact constraints
/// to that many after the step and twice that during the narrow phase's flush (this step's added before last step's
/// removed). With J joints and static contacts on a body, every constraint therefore fits while
/// 2 × (2 × <see cref="PhysicsWorld.ContactPairsPerBody"/> + J) − 2 &lt; <see cref="PhysicsWorld.SolverBatchLimit"/>:
/// J up to 127 at 192 and 1024. Only dynamic bodies count; a kinematic or static one occupies no batch.</para>
///
/// <para><b>What a refusal looks like.</b> The pair is not collided this step, exactly as a filtered pair is, and is
/// counted in <see cref="PhysicsWorld.ContactPairsRefused"/>. A pair with a STATIC is charged but never refused — a
/// body at its budget must not fall through the ground. It takes a body touching more than
/// <see cref="PhysicsWorld.ContactPairsPerBody"/> others at once, far past anything measured (a vehicle among a
/// versus round's patients: 38).</para>
///
/// <para><b>Determinism.</b> Charged from the narrow phase, which this engine steps single-threaded (hard rule 5), in
/// the broad phase's own pair order, so which pair is refused is the same every run of a seed.</para>
/// </summary>
internal sealed class ContactPairBudget
{
    private readonly int _perBody;

    /// <summary>Pairs charged to each body this step, by handle value; valid only where <see cref="_stamps"/> holds
    /// the current step, so a new step needs no clear.</summary>
    private int[] _counts = new int[256];

    private long[] _stamps = new long[256];

    private long _step = 1;

    public ContactPairBudget(int perBody) => _perBody = perBody;

    /// <summary>Pairs refused since the world was made.</summary>
    public long Refused { get; private set; }

    /// <summary>Starts a new step's count.</summary>
    public void BeginStep() => _step++;

    /// <summary>Charges a pair to each of its dynamic bodies (a handle value, or -1 for a side that is not dynamic),
    /// or refuses it — charging neither — when either is already at its budget. A pair with a static
    /// (<paramref name="withStatic"/>) is always admitted.</summary>
    public bool TryAdmit(int dynamicA, int dynamicB, bool withStatic)
    {
        if (!withStatic && (AtBudget(dynamicA) || AtBudget(dynamicB)))
        {
            Refused++;
            return false;
        }

        Charge(dynamicA);
        Charge(dynamicB);
        return true;
    }

    private bool AtBudget(int body) => body >= 0 && body < _stamps.Length && _stamps[body] == _step && _counts[body] >= _perBody;

    private void Charge(int body)
    {
        if (body < 0)
        {
            return;
        }

        if (body >= _stamps.Length)
        {
            var length = Math.Max(body + 1, _stamps.Length * 2);
            Array.Resize(ref _stamps, length);
            Array.Resize(ref _counts, length);
        }

        if (_stamps[body] != _step)
        {
            _stamps[body] = _step;
            _counts[body] = 0;
        }

        _counts[body]++;
    }
}
