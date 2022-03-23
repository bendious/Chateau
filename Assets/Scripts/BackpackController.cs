using System.Collections;
using UnityEngine;


[RequireComponent(typeof(Collider2D))]
public sealed class BackpackController : MonoBehaviour, IHolder, IInteractable, IAttachable
{
	public int m_holdCountMax = 2;
	public /*override*/ int HoldCountMax => m_holdCountMax;

	public Vector3 m_attachOffsetLocal = Vector3.forward * 0.2f;
	public /*override*/ Vector3 AttachOffsetLocal => m_attachOffsetLocal;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.forward * 0.2f/*?*/;

	public float m_colorMin = 0.25f;
	public float m_colorMax = 1.0f;


	private void Awake()
	{
		GetComponent<SpriteRenderer>().color = Utility.ColorRandom(m_colorMin, m_colorMax); // TODO: more deliberate choice?
	}


	public void Interact(KinematicCharacter interactor) => interactor.ChildAttach(this);

	public /*override*/ bool ChildAttach(IAttachable attachable)
	{
		bool attached = IHolder.ChildAttachInternal(attachable, this);
		if (!attached)
		{
			return false;
		}

		attachable.Component.gameObject.SetActive(false); // to disable collision and any effects that would be visible despite being behind the backpack

		return true;
	}

	public /*override*/ void ChildDetach(IAttachable attachable, bool noAutoReplace)
	{
		attachable.Component.gameObject.SetActive(true);

		IHolder.ChildDetachInternal(attachable, this, noAutoReplace);
	}


	// TODO: combine w/ ItemController version?
	public void AttachInternal(IHolder holder)
	{
		if (transform.parent != null)
		{
			// ensure any special detach logic gets invoked
			Detach(false);
		}

		Component holderComp = holder.Component;
		transform.SetParent(holderComp.transform);
		transform.localRotation = Quaternion.identity; // TODO: lerp?
		StartCoroutine(UpdateOffset());

		Rigidbody2D body = GetComponent<Rigidbody2D>();
		body.velocity = Vector2.zero;
		body.angularVelocity = 0.0f;
		body.bodyType = RigidbodyType2D.Kinematic;

		gameObject.layer = holderComp.gameObject.layer;

		AvatarController avatar = holderComp.GetComponent<AvatarController>();
		if (avatar != null)
		{
			avatar.InventorySync();
		}
	}

	public /*override*/ void Detach(bool noAutoReplace)
	{
		transform.parent.GetComponent<IHolder>().ChildDetach(this, noAutoReplace);
	}

	// this (although public) should only be called by IHolderController.ItemDetachInternal() // TODO?
	public void DetachInternal()
	{
		AvatarController avatar = transform.parent.GetComponent<AvatarController>(); // NOTE that we have to grab this BEFORE detaching, but sync the inventory AFTER

		StopAllCoroutines();

		// TODO: combine w/ ItemController.DetachInternal()?
		transform.SetParent(null);
		Rigidbody2D body = GetComponent<Rigidbody2D>();
		body.bodyType = RigidbodyType2D.Dynamic;

		if (avatar != null)
		{
			avatar.InventorySync();
		}
	}


	private IEnumerator UpdateOffset()
	{
		while (true)
		{
			transform.localPosition = (Vector3)transform.parent.GetComponent<KinematicCharacter>().m_armOffset + AttachOffsetLocal; // TODO: lerp?

			yield return null;
		}
	}
}
