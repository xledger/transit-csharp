namespace Transit.Net.Impl;

/// <summary>
/// Interface for emitter-aware write handlers. Each emitter binds its own instance, so a handler
/// that implements this may keep per-write state.
/// </summary>
internal interface IAbstractEmitterAware
{
    IWriteHandler BindTo(AbstractEmitter abstractEmitter);
}
