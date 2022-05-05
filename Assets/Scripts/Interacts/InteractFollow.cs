using System;
using System.Collections;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractFollow : MonoBehaviour, IInteractable, IKey
{
	public IUnlockable Lock { get; set; }


	[SerializeField]
	private float m_maxSnapDistance = 0.5f;
	[SerializeField]
	private float m_maxSnapDegrees = 45.0f;


	private Vector3 m_correctPosition;
	private float m_correctRotationDegrees;

	private KinematicCharacter m_followCharacter;
	private float m_followOffsetDegrees;


	public bool IsInPlace
	{
		get => m_followCharacter == null && (transform.position - m_correctPosition).magnitude < m_maxSnapDistance && Utility.FloatEqual(transform.rotation.eulerAngles.z, m_correctRotationDegrees, m_maxSnapDegrees);
		set => IsInPlace = IsInPlace; // TODO?
	}


	private void Awake()
	{
		m_correctPosition = transform.position;
		m_correctRotationDegrees = transform.rotation.eulerAngles.z;
		transform.SetPositionAndRotation(GameController.Instance.RoomFromPosition(transform.position).InteriorPosition((Lock as LockController).m_keyHeightMax, gameObject), Quaternion.Euler(0.0f, 0.0f, UnityEngine.Random.Range(0.0f, 360.0f)));
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
			Tuple<Vector3, float> followTf = FollowTransform();
			m_followOffsetDegrees = transform.rotation.eulerAngles.z - followTf.Item2;
			StartCoroutine(Follow());
		}
		else
		{
			interactor.GetComponent<AvatarController>().m_follower = null;
			m_followCharacter = null;
			if (IsInPlace)
			{
				transform.SetPositionAndRotation(m_correctPosition, Quaternion.Euler(0.0f, 0.0f, m_correctRotationDegrees));
			}
			(Lock as LockController).CheckInput();
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


	private Tuple<Vector3, float> FollowTransform()
	{
		Vector3 offset = m_followCharacter is AvatarController avatar ? avatar.m_aimObject.transform.position - avatar.transform.position : Vector3.zero;
		if (offset.magnitude > 1.0f) // TODO: use maximum focus distance?
		{
			offset = offset.normalized;
		}
		return Tuple.Create(m_followCharacter.transform.position + offset, Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg);
	}

	private IEnumerator Follow()
	{
		while (m_followCharacter != null) // TODO: limit to start room?
		{
			Tuple<Vector3, float> followTf = FollowTransform();
			transform.SetPositionAndRotation(followTf.Item1, Quaternion.Euler(0.0f, 0.0f, followTf.Item2 + m_followOffsetDegrees));
			yield return null;
		}
	}
}
