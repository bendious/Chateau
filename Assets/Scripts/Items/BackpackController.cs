using System.Collections;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public sealed class BackpackController : MonoBehaviour, IHolder, IInteractable, IAttachable, ISavable
{
	public int m_holdCountMax = 2;
	public /*override*/ int HoldCountMax => m_holdCountMax;

	public string Name => m_name;
	[SerializeField] private string m_name;

	public Vector3 m_attachOffsetLocal = Vector3.forward * 0.2f;
	public /*override*/ Vector3 AttachOffsetLocal => m_attachOffsetLocal;
	public /*override*/ Vector3 ChildAttachPointLocal => Vector3.zero;


	int ISavable.Type { get; set; }


	public void Interact(KinematicCharacter interactor, bool reverse) => interactor.ChildAttach(this);

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

	void ISavable.SaveInternal(SaveWriter saveFile)
	{
		// NOTE that since child objects are disabled, they are not collected by GameController.Save() and we have to handle them ourselves
		saveFile.Write(transform.childCount);
		for (int i = 0; i < transform.childCount; ++i)
		{
			ISavable.Save(saveFile, transform.GetChild(i).GetComponent<ISavable>());
		}
	}

	void ISavable.LoadInternal(SaveReader saveFile)
	{
		// NOTE that since child objects are disabled, they are not collected by GameController.Save() and we have to handle them ourselves
		saveFile.Read(out int numChildren);
		for (int i = 0; i < numChildren; ++i)
		{
			ISavable savable = ISavable.Load(saveFile);
			ChildAttach(savable.Component.GetComponent<IAttachable>());
		}
	}


	private IEnumerator UpdateOffset()
	{
		while (true)
		{
			transform.localPosition = (Vector3)transform.parent.GetComponent<KinematicCharacter>().ArmOffset + AttachOffsetLocal; // TODO: lerp? merge w/ AnimatedOffset.cs?

			yield return null;
		}
	}
}
