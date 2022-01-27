using System.Linq;
using UnityEngine;


public interface IHolderController
{
	public abstract int HoldCountMax { get; }

	public abstract GameObject Object { get; }

	public abstract Vector3 AttachOffsetLocal { get; }
	public abstract Vector3 ChildAttachPointLocal { get; }

	public virtual float Speed => 0.0f; // TODO: move out of IHolderController?


	public virtual bool ItemAttach(ItemController item)
	{
		return ItemAttachInternal(item, this);
	}

	public virtual void ItemDetach(ItemController item)
	{
		ItemDetachInternal(item, this);
	}


	protected static bool ItemAttachInternal(ItemController item, IHolderController holder)
	{
		// prevent over-holding
		if (holder.Object.transform.childCount >= holder.HoldCountMax)
		{
			return false;
		}

		if (item.transform.parent != null)
		{
			// ensure any special detach logic gets invoked
			item.transform.parent.GetComponent<IHolderController>().ItemDetach(item);
		}

		item.AttachInternal(holder);

		return true;
	}

	protected static void ItemDetachInternal(ItemController item, IHolderController holder)
	{
		item.DetachInternal();

		// maybe attach item from other holder
		Transform holderTf = holder.Object.transform;
		if (holderTf.parent != null)
		{
			// find any valid other holders
			int thisSiblingIdx = holderTf.GetSiblingIndex();
			IHolderController[] lowerHolders = holderTf.parent.GetComponentsInChildren<IHolderController>().Where(otherHolder => otherHolder is not ArmController && otherHolder.Object.transform.GetSiblingIndex() > thisSiblingIdx).ToArray();

			foreach (IHolderController otherHolder in lowerHolders)
			{
				// try attaching each child item until one works
				foreach (ItemController newItem in otherHolder.Object.GetComponentsInChildren<ItemController>())
				{
					newItem.Detach();
					bool attached = holder.ItemAttach(newItem);
					if (attached)
					{
						return;
					}
					else
					{
						// reattach to original to avoid orphaning upon failure
						otherHolder.ItemAttach(newItem);
					}
				}
			}
		}

		return;
	}
}
