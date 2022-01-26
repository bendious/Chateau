using UnityEngine;


public interface IHolderController
{
	public abstract int HoldCountMax { get; }

	public abstract GameObject Object { get; }

	public abstract Vector3 AttachPointLocal { get; }

	public virtual float Speed => 0.0f; // TODO: move out of IHolderController?


	public virtual bool ItemAttach(ItemController item)
	{
		return ItemAttachInternal(item, this);
	}

	public virtual bool ItemDetach(ItemController item)
	{
		item.Detach();
		return true;
	}


	public static bool ItemAttachInternal(ItemController item, IHolderController holder)
	{
		// prevent over-holding
		if (holder.Object.transform.childCount >= holder.HoldCountMax)
		{
			return false;
		}

		if (item.transform.parent != null)
		{
			// ensure any special detach logic gets invoked
			bool detached = item.transform.parent.GetComponent<IHolderController>().ItemDetach(item);
			if (!detached)
			{
				return false;
			}
		}

		item.AttachTo(holder);

		return true;
	}
}
