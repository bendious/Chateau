using System.Collections;
using System.IO;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public sealed class BackpackController : MonoBehaviour, IHolder, IInteractable, IAttachable, ISavable
{
	public int m_holdCountMax = 2;
	public /*override*/ int HoldCountMax => m_holdCountMax;

	public Vector3 m_attachOffsetLocal = Vector3.forward * 0.2f;
	public /*override*/ Vector3 AttachOffsetLocal => m_attachOffsetLocal;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.zero;


	int ISavable.Type { get; set; }


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


	public void AttachInternal(IHolder holder)
	{
		IAttachable.AttachInternalShared(this, holder, GetComponent<Rigidbody2D>());

		StartCoroutine(UpdateOffset());
	}

	public /*override*/ void Detach(bool noAutoReplace)
	{
		transform.parent.GetComponent<IHolder>().ChildDetach(this, noAutoReplace);

		StopAllCoroutines();
	}

	void ISavable.SaveInternal(BinaryWriter saveFile)
	{
	}

	void ISavable.LoadInternal(BinaryReader saveFile)
	{
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
