using PokeChess.Autobattler;

public abstract class UnitSkillRuntime
{
    protected UnitSkillRuntime(UnitController caster, UnitSkillDefinition definition, UnitController preferredTarget)
    {
        Caster = caster;
        Definition = definition;
        PreferredTarget = preferredTarget;
    }

    protected UnitController Caster { get; }
    protected UnitSkillDefinition Definition { get; }
    protected UnitController PreferredTarget { get; }
    protected Fusion.NetworkRunner Runner => Caster != null ? Caster.Runner : null;

    private bool _started;

    public bool Start()
    {
        if (_started)
            return false;

        _started = true;
        return OnStart();
    }

    public bool Tick()
    {
        if (!_started || Caster == null || !Caster.CanContinueSkillRuntime())
            return false;

        return OnTick();
    }

    public void Stop()
    {
        if (!_started)
            return;

        OnStop();
        _started = false;
    }

    protected abstract bool OnStart();
    protected abstract bool OnTick();
    protected virtual void OnStop() { }
}

public abstract class DurationSkillRuntime : UnitSkillRuntime
{
    private readonly float _durationSeconds;
    private Fusion.TickTimer _durationTimer;

    protected DurationSkillRuntime(UnitController caster, UnitSkillDefinition definition, UnitController preferredTarget, float durationSeconds)
        : base(caster, definition, preferredTarget)
    {
        _durationSeconds = durationSeconds;
    }

    protected sealed override bool OnStart()
    {
        if (_durationSeconds <= 0f || Runner == null)
            return false;

        if (!OnStarted())
            return false;

        _durationTimer = Fusion.TickTimer.CreateFromSeconds(Runner, _durationSeconds);
        return true;
    }

    protected sealed override bool OnTick()
    {
        if (!_durationTimer.IsRunning || _durationTimer.ExpiredOrNotRunning(Runner))
            return false;

        return OnTickWhileActive();
    }

    protected sealed override void OnStop()
    {
        OnStopped();
    }

    protected virtual bool OnStarted() => true;
    protected abstract bool OnTickWhileActive();
    protected virtual void OnStopped() { }
}

public abstract class ChannelingSkillRuntime : DurationSkillRuntime
{
    private readonly bool _lockMovement;
    private readonly bool _lockAttack;

    protected ChannelingSkillRuntime(
        UnitController caster,
        UnitSkillDefinition definition,
        UnitController preferredTarget,
        float durationSeconds,
        bool lockMovement = true,
        bool lockAttack = true)
        : base(caster, definition, preferredTarget, durationSeconds)
    {
        _lockMovement = lockMovement;
        _lockAttack = lockAttack;
    }

    protected override bool OnStarted()
    {
        Caster.SetSkillActionLock(_lockMovement, _lockAttack);
        return OnChannelStarted();
    }

    protected override void OnStopped()
    {
        Caster.ClearSkillActionLock();
        OnChannelStopped();
    }

    protected virtual bool OnChannelStarted() => true;
    protected virtual void OnChannelStopped() { }
}