namespace Foster.Framework;

public abstract class Module
{
	/// <summary>
	/// Called when the Application is starting up, or when the 
	/// Module was Registered if the Application is already running.
	/// </summary>
	public virtual void Startup() { }

	/// <summary>
	/// Called then the Application is shutting down
	/// </summary>
	public virtual void Shutdown() { }

	/// <summary>
	/// Called once per frame (before FixedUpdate())
	/// </summary>
	public virtual void Update() { }

	/// <summary>
	/// Called once every FixedStepTarget, can be called multiple times per frame if frametime is longer than FixedStepTarget
	/// </summary>
	public virtual void FixedUpdate() { }

	/// <summary>
	/// Called once per frame (after FixedUpdate())
	/// </summary>
	public virtual void LateUpdate() { }

	/// <summary>
	/// Called once per frame (after FixedUpdate())
	/// </summary>
	public virtual void Render() { }
}