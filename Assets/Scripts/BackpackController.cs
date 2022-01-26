using System.Collections;
using UnityEngine;


public sealed class BackpackController : ItemController, IHolderController
{
	public int m_holdCountMax = 2;
	public /*override*/ int HoldCountMax => m_holdCountMax;

	public /*override*/ GameObject Object => gameObject;

	public Vector3 m_attachOffsetLocal = Vector3.forward * 0.2f;
	public /*override*/ Vector3 AttachOffsetLocal => m_attachOffsetLocal;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.forward * 0.2f/*?*/;


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

	public /*override*/ void ItemDetach(ItemController item)
	{
		item.enabled = true;

		IHolderController.ItemDetachInternal(item, this);
	}


	// TODO: combine w/ ItemController holder-based version?
	public void AttachTo(KinematicCharacter character)
	{
		transform.SetParent(character.transform);
		transform.localRotation = Quaternion.identity; // TODO: lerp?
		StartCoroutine(UpdateOffset());

		m_body.velocity = Vector2.zero;
		m_body.angularVelocity = 0.0f;
		m_body.bodyType = RigidbodyType2D.Kinematic;

		gameObject.layer = character.gameObject.layer;
	}

	public override void Detach()
	{
		StopAllCoroutines();
		DetachInternal();
	}


	private IEnumerator UpdateOffset()
	{
		while (true)
		{
			// TODO: unify {Avatar/Enemy}Controller.m_armOffset
			AvatarController avatar = transform.parent.GetComponent<AvatarController>();
			transform.localPosition = (Vector3)(avatar == null ? transform.parent.GetComponent<EnemyController>().m_armOffset : avatar.m_armOffset) + AttachOffsetLocal; // TODO: lerp?

			yield return null;
		}
	}
}
