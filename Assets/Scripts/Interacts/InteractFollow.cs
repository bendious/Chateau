using System.Collections;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractFollow : MonoBehaviour, IInteractable, IKey
{
	public IUnlockable Lock { get; set; }


	private const float m_maxSnapDistance = 0.25f;

	private Vector3 m_correctPosition;

	private KinematicCharacter m_followCharacter;


	public bool IsInPlace { get => m_followCharacter == null && (transform.position - m_correctPosition).magnitude < m_maxSnapDistance; set => IsInPlace = IsInPlace;/*TODO*/ }


	private void Awake()
	{
		m_correctPosition = transform.position;
		transform.position = GameController.Instance.RoomFromPosition(transform.position).InteriorPosition((Lock as LockController).m_keyHeightMax, gameObject);
	}


	public bool CanInteract(KinematicCharacter interactor)
	{
		if (!enabled)
		{
			return false;
		}
		AvatarController avatar = interactor.GetComponent<AvatarController>();
		return avatar.m_follower == null || avatar.m_follower == this;
	}

	public void Interact(KinematicCharacter interactor)
	{
		if (m_followCharacter == null)
		{
			m_followCharacter = interactor;
			interactor.GetComponent<AvatarController>().m_follower = this;
			StartCoroutine(Follow());
		}
		else
		{
			interactor.GetComponent<AvatarController>().m_follower = null;
			m_followCharacter = null;
			if (IsInPlace)
			{
				transform.position = m_correctPosition;
			}
			(Lock as LockController/*?*/).CheckInput();
		}
	}


	public void Use()
	{
		Debug.Assert(false);
	}

	public void Deactivate()
	{
		m_followCharacter = null;
		GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
		enabled = false;
	}


	private IEnumerator Follow()
	{
		while (m_followCharacter != null) // TODO: limit to start room?
		{
			Vector3 offset = m_followCharacter is AvatarController avatar ? avatar.m_aimObject.transform.position - avatar.transform.position : Vector3.zero;
			if (offset.magnitude > 1.0f/*?*/)
			{
				offset = offset.normalized;
			}
			transform.position = m_followCharacter.transform.position + offset;

			yield return null;
		}
	}
}
