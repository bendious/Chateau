using System;
using System.Collections;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractFollow : MonoBehaviour, IInteractable, IKey
{
	public IUnlockable Lock { get; set; }


	[SerializeField] private InteractFollow[] m_snapPrecursors;
	[SerializeField] private float m_maxSnapDistance = 0.5f;
	[SerializeField] private float m_maxSnapDegrees = 45.0f;


	private Vector3 m_correctPosition;
	private float m_correctRotationDegrees;

	private KinematicCharacter m_followCharacter;
	private Vector3 m_followOffset;
	private float m_followOffsetDegrees;


	public bool IsInPlace
	{
		get => m_followCharacter == null && (transform.position - m_correctPosition).magnitude < m_maxSnapDistance && transform.rotation.eulerAngles.z.FloatEqualDegrees(m_correctRotationDegrees, m_maxSnapDegrees);
		set => IsInPlace = IsInPlace; // TODO?
	}


	private void Awake()
	{
		m_correctPosition = transform.position;
		m_correctRotationDegrees = transform.rotation.eulerAngles.z;
	}

	private void Start()
	{
		transform.SetPositionAndRotation(GameController.Instance.RoomFromPosition(transform.position).InteriorPosition((Lock as LockController).m_keyHeightMax, gameObject), Quaternion.Euler(0.0f, 0.0f, UnityEngine.Random.Range(0.0f, 360.0f)));
	}


	public bool CanInteract(KinematicCharacter interactor)
	{
		if (!enabled)
		{
			return false;
		}
		AvatarController avatar = interactor.GetComponent<AvatarController>();
		if (avatar.m_follower != null && avatar.m_follower != this)
		{
			return false;
		}
		return transform.parent.parent == GameController.Instance.RoomFromPosition(interactor.transform.position).transform;
	}

	public float Priority(KinematicCharacter interactor) => m_followCharacter == interactor ? float.MaxValue : IsInPlace ? 0.5f : 1.0f;

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		if (m_followCharacter == null)
		{
			m_followCharacter = interactor;
			interactor.GetComponent<AvatarController>().m_follower = this;
			Tuple<Vector3, float> followTf = FollowTransform();
			m_followOffset = Quaternion.Inverse(Quaternion.Euler(0.0f, 0.0f, followTf.Item2)) * (transform.position - followTf.Item1);
			m_followOffsetDegrees = transform.rotation.eulerAngles.z - followTf.Item2;

			// edit sorting order to put this "on top" w/o changing other relationships
			InteractFollow[] siblings = transform.parent.parent.GetComponentsInChildren<InteractFollow>(); // TODO: work even between different rooms?
			int orderItr = 0;
			foreach (InteractFollow interact in siblings.OrderBy(i => i == this ? int.MaxValue : i.GetComponent<SpriteRenderer>().sortingOrder))
			{
				foreach (Renderer renderer in interact.GetComponentsInChildren<Renderer>().Where(r => r is not SpriteMask)) // NOTE that this includes SpriteRenderers as well as MeshRenderers for text
				{
					renderer.sortingOrder = orderItr;
					SpriteMask mask = renderer.GetComponent<SpriteMask>();
					if (mask != null)
					{
						mask.frontSortingOrder = orderItr;
						mask.backSortingOrder = orderItr - 1;
					}
					orderItr += 10; // NOTE that we don't use sorting order offsets of 1 except for the avatar focus indicator // TODO: parameterize offset amount?
				}
			}

			StartCoroutine(Follow());
		}
		else
		{
			StopFollowing();
		}
	}


	public void Use()
	{
		Debug.Assert(false, "Trying to Use() an InteractFollow.");
	}

	public void Deactivate()
	{
		transform.SetPositionAndRotation(m_correctPosition, Quaternion.Euler(0.0f, 0.0f, m_correctRotationDegrees)); // in case we were put within range w/o snapping
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
		Bounds followBounds = transform.parent.parent.GetComponent<RoomController>().BoundsInterior; // TODO: parameterize?

		while (m_followCharacter != null && followBounds.Contains(m_followCharacter.transform.position))
		{
			Tuple<Vector3, float> followTf = FollowTransform();
			transform.SetPositionAndRotation(followBounds.ClosestPoint(followTf.Item1 + Quaternion.Euler(0.0f, 0.0f, followTf.Item2) * m_followOffset), Quaternion.Euler(0.0f, 0.0f, followTf.Item2 + m_followOffsetDegrees));
			yield return null;
		}

		StopFollowing();
	}

	private void StopFollowing()
	{
		if (m_followCharacter == null)
		{
			return;
		}

		m_followCharacter.GetComponent<AvatarController>().m_follower = null;
		m_followCharacter = null;

		if (IsInPlace && (m_snapPrecursors.Length <= 0 || m_snapPrecursors.Any(interact => interact.IsInPlace)))
		{
			transform.SetPositionAndRotation(m_correctPosition, Quaternion.Euler(0.0f, 0.0f, m_correctRotationDegrees));
			(Lock as LockController).CheckInput();
		}
	}
}
