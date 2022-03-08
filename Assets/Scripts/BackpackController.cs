using System.Collections;
using UnityEngine;


public sealed class BackpackController : MonoBehaviour, IHolder, IInteractable
{
	public int m_holdCountMax = 2;
	public /*override*/ int HoldCountMax => m_holdCountMax;

	public Vector3 m_attachOffsetLocal = Vector3.forward * 0.2f;
	public /*override*/ Vector3 AttachOffsetLocal => m_attachOffsetLocal;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.forward * 0.2f/*?*/;


	private void Awake()
	{
		GetComponent<SpriteRenderer>().color = new Color(Random.Range(0.5f, 1.0f), Random.Range(0.5f, 1.0f), Random.Range(0.5f, 1.0f)); // TODO: more deliberate choice?
	}


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

	public /*override*/ void ItemDetach(ItemController item, bool noAutoReplace)
	{
		item.gameObject.SetActive(true);

		IHolder.ItemDetachInternal(item, this, noAutoReplace);
	}


	// TODO: combine w/ ItemController holder-based version?
	public void AttachTo(KinematicCharacter character)
	{
		if (transform.parent != null)
		{
			// ensure any special detach logic gets invoked
			Detach(false);
		}

		transform.SetParent(character.transform);
		transform.localRotation = Quaternion.identity; // TODO: lerp?
		StartCoroutine(UpdateOffset());

		Rigidbody2D body = GetComponent<Rigidbody2D>();
		body.velocity = Vector2.zero;
		body.angularVelocity = 0.0f;
		body.bodyType = RigidbodyType2D.Kinematic;

		gameObject.layer = character.gameObject.layer;

		AvatarController avatar = character.GetComponent<AvatarController>();
		if (avatar != null)
		{
			avatar.InventorySync();
		}
	}

	public /*override*/ void Detach(bool noAutoReplace)
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
