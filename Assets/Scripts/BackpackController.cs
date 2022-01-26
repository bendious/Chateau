using UnityEngine;


public sealed class BackpackController : ItemController, IHolderController
{
	public int m_holdCountMax = 2;
	public /*override*/ int HoldCountMax => m_holdCountMax;

	public /*override*/ GameObject Object => gameObject;

	public /*override*/ Vector3 AttachPointLocal => Vector3.forward * 0.2f/*?*/;


	public /*override*/ bool ItemAttach(ItemController item)
	{
		bool attached = IHolderController.ItemAttachInternal(item, this);
		if (!attached)
		{
			return false;
		}

		item.enabled = false; // to disable any effects that would be visible despite being behind the backpack

		return true;
	}

	public /*override*/ bool ItemDetach(ItemController item)
	{
		item.enabled = true;

		item.Detach();

		return true;
	}


	// TODO: combine w/ ItemController holder-based version?
	public void AttachTo(AnimationController character)
	{
		transform.SetParent(character.transform);
		transform.localPosition = Vector3.forward * 0.2f; // TODO: lerp? use m_armOffset if possible?
		transform.localRotation = Quaternion.identity; // TODO: lerp?
		m_body.velocity = Vector2.zero;
		m_body.angularVelocity = 0.0f;
		m_body.bodyType = RigidbodyType2D.Kinematic;
		gameObject.layer = character.gameObject.layer;
	}

	public void DetachFrom(AnimationController character)
	{
	}
}
