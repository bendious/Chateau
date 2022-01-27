using System.Collections;
using UnityEngine;


public sealed class BackpackController : MonoBehaviour, IHolder, IAttachable
{
	public int m_holdCountMax = 2;
	public /*override*/ int HoldCountMax => m_holdCountMax;

	public /*override*/ GameObject Object => gameObject;

	public Vector3 m_attachOffsetLocal = Vector3.forward * 0.2f;
	public /*override*/ Vector3 AttachOffsetLocal => m_attachOffsetLocal;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.forward * 0.2f/*?*/;


	public /*override*/ bool ItemAttach(ItemController item)
	{
		bool attached = IHolder.ItemAttachInternal(item, this);
		if (!attached)
		{
			return false;
		}

		item.gameObject.SetActive(false); // to disable collision and any effects that would be visible despite being behind the backpack

		return true;
	}

	public /*override*/ void ItemDetach(ItemController item)
	{
		item.gameObject.SetActive(true);

		IHolder.ItemDetachInternal(item, this);
	}


	// TODO: combine w/ ItemController holder-based version?
	public void AttachTo(KinematicCharacter character)
	{
		transform.SetParent(character.transform);
		transform.localRotation = Quaternion.identity; // TODO: lerp?
		StartCoroutine(UpdateOffset());

		Rigidbody2D body = GetComponent<Rigidbody2D>();
		body.velocity = Vector2.zero;
		body.angularVelocity = 0.0f;
		body.bodyType = RigidbodyType2D.Kinematic;

		gameObject.layer = character.gameObject.layer;
	}

	public void Detach()
	{
		StopAllCoroutines();

		// TODO: combine w/ ItemController.DetachInternal()?
		transform.SetParent(null);
		transform.position = (Vector2)transform.position; // nullify any z that may have been applied for rendering order
		Rigidbody2D body = GetComponent<Rigidbody2D>();
		body.bodyType = RigidbodyType2D.Dynamic;
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
