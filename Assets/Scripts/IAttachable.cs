using UnityEngine;


public interface IAttachable
{
	public Component Component => this as Component;

	// NOTE that there is no Attach() since the entry point is IHolder.ChildAttach()
	public void Detach(bool noAutoReplace);

	// these (although public) should only be called by IHolder.Child{Attach/Detach}{Internal}() // TODO?
	public void AttachInternal(IHolder holder);
	public void DetachInternal();
}
