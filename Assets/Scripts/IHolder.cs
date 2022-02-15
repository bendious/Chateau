using UnityEngine;


public interface IHolder
{
	public int HoldCountMax { get; }

	public Component Component => this as Component;

	public Vector3 AttachOffsetLocal { get; }
	public Vector3 ChildAttachPointLocal { get; }

	public float Speed => 0.0f; // TODO: move out of IHolderController?


	public bool ItemAttach(ItemController item);
	public void ItemDetach(ItemController item, bool noAutoReplace);


	protected static bool ItemAttachInternal(ItemController item, IHolder holder)
	{
		// prevent over-holding
		if (holder.Component.transform.childCount >= holder.HoldCountMax)
		{
			return false;
		}

		if (item.transform.parent != null)
		{
			// ensure any special detach logic gets invoked
			item.transform.parent.GetComponent<IHolder>().ItemDetach(item, false);
		}

		item.AttachInternal(holder);

		AvatarController avatar = holder.Component.transform.parent.GetComponent<AvatarController>();
		if (avatar != null)
		{
			avatar.InventorySync();
		}

		return true;
	}

	protected static void ItemDetachInternal(ItemController item, IHolder holder, bool noAutoReplace)
	{
		item.DetachInternal();

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
		Collider2D[] itemColliders = item.GetComponents<Collider2D>();
		int thisSiblingIdx = holderTf.GetSiblingIndex();
		bool foundReplacement = false;
		foreach (IHolder otherHolder in holderTf.parent.GetComponentsInChildren<IHolder>())
		{
			// avoid immediate collision w/ items remaining in other holders(s)
			Component otherHolderComp = otherHolder.Component;
			ItemController[] items = otherHolderComp.GetComponentsInChildren<ItemController>(true);
			foreach (ItemController otherItem in items)
			{
				if (otherItem != item && otherItem.gameObject.activeInHierarchy)
				{
					EnableCollision.TemporarilyDisableCollision(itemColliders, otherItem.GetComponents<Collider2D>());
				}
			}

			if (foundReplacement || otherHolder is ArmController || otherHolderComp.transform.GetSiblingIndex() <= thisSiblingIdx)
			{
				continue; // don't move items out of hands or down from higher holders
			}

			// try attaching each child item until one works
			foreach (ItemController newItem in items)
			{
				newItem.Detach(true);
				foundReplacement = holder.ItemAttach(newItem);
				if (foundReplacement)
				{
					break;
				}

				// reattach to original to avoid orphaning upon failure
				otherHolder.ItemAttach(newItem);
			}
		}

		AvatarController avatar = holderTf.parent.GetComponent<AvatarController>();
		if (avatar != null)
		{
			avatar.InventorySync(); // TODO: avoid multi-sync when detaching multiple (e.g. upon death)?
		}

		return;
	}
}
