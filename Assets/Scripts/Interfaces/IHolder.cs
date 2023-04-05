using System.Linq;
using UnityEngine;


public interface IHolder
{
	public int HoldCountMax { get; }

	public Component Component => this as Component;

	public Vector3 AttachOffsetLocal { get; }
	public Vector3 ChildAttachPointLocal { get; }

	public float Speed => 0.0f; // TODO: move out of IHolder?

	public bool IsSwinging => false;


	public bool ChildAttach(IAttachable attachable);
	public void ChildDetach(IAttachable attachable, bool noAutoReplace, IHolder holderNew);


	protected static bool ChildAttachInternal(IAttachable attachable, IHolder holder)
	{
		// prevent over-holding
		Component holderComp = holder.Component;
		if (holderComp.GetComponentsInDirectChildren<IAttachable>(a => a.Component, true).Count() >= holder.HoldCountMax)
		{
			return false;
		}

		Component attachableComp = attachable.Component;
		if (attachableComp.transform.parent != null)
		{
			// ensure any special detach logic gets invoked
			attachable.Detach(false, holder);
		}

		bool attached = attachable.AttachInternal(holder);
		if (!attached)
		{
			return false;
		}

		Transform holderParent = holderComp.transform.parent;
		AvatarController avatar = holderParent == null ? null : holderParent.GetComponent<AvatarController>();
		if (avatar != null)
		{
			avatar.InventorySync(); // TODO: prevent multi-sync during inventory swap/scroll
		}

		return true;
	}

	protected static void ChildDetachInternal(IAttachable attachable, IHolder holder, bool noAutoReplace, IHolder holderNew)
	{
		IAttachable.DetachInternalShared(attachable);

		if (noAutoReplace)
		{
			return;
		}

		// maybe attach item from other holder
		Transform holderTf = holder.Component.transform;
		if (holderTf.parent == null)
		{
			return;
		}

		// find any valid other holders
		Collider2D[] attachableColliders = attachable == null ? null : attachable.Component.GetComponents<Collider2D>();
		int thisSiblingIdx = holderTf.GetSiblingIndex();
		bool foundReplacement = false;
		foreach (IHolder otherHolder in holderTf.parent.GetComponentsInChildren<IHolder>())
		{
			Component otherHolderComp = otherHolder.Component;
			if (otherHolderComp.gameObject == holderTf.parent.gameObject)
			{
				continue;
			}

			// avoid immediate collision w/ items remaining in other holders(s)
			IAttachable[] otherAttachables = otherHolderComp.GetComponentsInChildren<IAttachable>(true).Where(attachable => attachable is not IHolder).ToArray();
			foreach (IAttachable otherAttachable in otherAttachables)
			{
				Component otherAttachableComp = otherAttachable.Component;
				if (otherAttachable != attachable && otherAttachableComp.gameObject.activeInHierarchy)
				{
					EnableCollision.TemporarilyDisableCollision(attachableColliders, otherAttachableComp.GetComponents<Collider2D>());
				}
			}

			if (foundReplacement || otherHolder is ArmController || otherHolderComp.transform.GetSiblingIndex() <= thisSiblingIdx)
			{
				continue; // don't move items out of hands or down from higher holders
			}

			// try attaching each child item until one works
			foreach (IAttachable newAttachable in otherAttachables)
			{
				newAttachable.Detach(true, holderNew);
				foundReplacement = holder.ChildAttach(newAttachable);
				if (foundReplacement)
				{
					break;
				}

				// reattach to original to avoid orphaning upon failure
				otherHolder.ChildAttach(newAttachable);
			}
		}

		// TODO: combine via KinematicCharacter virtual function?
		if (holderTf.parent.TryGetComponent(out AvatarController avatar))
		{
			avatar.InventorySync(); // TODO: avoid multi-sync when detaching multiple (e.g. upon death)?
		}
		else if (holderTf.parent.TryGetComponent(out AIController ai))
		{
			ai.OnChildDetached(holderNew);
		}

		return;
	}
}
