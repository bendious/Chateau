using System.Linq;
using UnityEngine;


public interface IHolder
{
	public int HoldCountMax { get; }

	public GameObject Object { get; }

	public Vector3 AttachOffsetLocal { get; }
	public Vector3 ChildAttachPointLocal { get; }

	public float Speed => 0.0f; // TODO: move out of IHolderController?


	public bool ItemAttach(ItemController item)
	{
		return ItemAttachInternal(item, this);
	}

	public void ItemDetach(ItemController item)
	{
		ItemDetachInternal(item, this);
	}


	protected static bool ItemAttachInternal(ItemController item, IHolder holder)
	{
		// prevent over-holding
		if (holder.Object.transform.childCount >= holder.HoldCountMax)
		{
			return false;
		}

		if (item.transform.parent != null)
		{
			// ensure any special detach logic gets invoked
			item.transform.parent.GetComponent<IHolder>().ItemDetach(item);
		}

		item.AttachInternal(holder);

		return true;
	}

	protected static void ItemDetachInternal(ItemController item, IHolder holder)
	{
		item.DetachInternal();

		// maybe attach item from other holder
		Transform holderTf = holder.Object.transform;
		if (holderTf.parent != null)
		{
			// find any valid other holders
			int thisSiblingIdx = holderTf.GetSiblingIndex();
			IHolder[] lowerHolders = holderTf.parent.GetComponentsInChildren<IHolder>().Where(otherHolder => otherHolder is not ArmController && otherHolder.Object.transform.GetSiblingIndex() > thisSiblingIdx).ToArray();

			foreach (IHolder otherHolder in lowerHolders)
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
