using System;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractFollow : MonoBehaviour, IInteractable, IKey
{
	public IUnlockable Lock { get; set; }


	[SerializeField] private InteractFollow[] m_snapPrecursors;
	[SerializeField] private float m_maxSnapDistance = 0.5f;
	[SerializeField] private float m_maxSnapDegrees = 45.0f;
	[SerializeField] private int m_splitCountMax;


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
		if (m_splitCountMax > 0) // TODO: separate component?
		{
			SpriteRenderer rendererLocal = GetComponent<SpriteRenderer>();
			Tuple<Sprite, Bounds>[] spritesAndBounds = VoronoiMasks(UnityEngine.Random.Range(2, m_splitCountMax + 1), rendererLocal.size.x, rendererLocal.sprite.pixelsPerUnit); // TODO: don't assume square SpriteRenderer size? influence number of pieces based on intended difficulty?
			m_splitCountMax = 0; // NOTE that this is BEFORE duplication
			bool flipX = UnityEngine.Random.value < 0.5f;
			bool flipY = UnityEngine.Random.value < 0.5f;
			foreach (Tuple<Sprite, Bounds> pair in spritesAndBounds)
			{
				// TODO: skip if no pixels are visible? update m_snapPrecursors
				InteractFollow newInteract = pair == spritesAndBounds.First() ? this : Instantiate(gameObject, transform.parent).GetComponent<InteractFollow>();
				newInteract.Lock = Lock;
				newInteract.GetComponent<SpriteMask>().sprite = pair.Item1;

				SpriteRenderer rendererChild = newInteract.GetComponent<SpriteRenderer>();
				rendererChild.flipX = flipX;
				rendererChild.flipY = flipY;

				BoxCollider2D trigger = newInteract.GetComponent<BoxCollider2D>();
				trigger.size = pair.Item2.size;
				trigger.offset = pair.Item2.center; // NOTE that this is unaffected by flip{X/Y} since only the rendered sprite is flipped and not the masks

				foreach (Transform childTf in newInteract.GetComponentsInChildren<Transform>())
				{
					if (childTf == newInteract.transform)
					{
						continue;
					}
					childTf.localPosition = pair.Item2.center;
				}
			}

			enabled = false; // to fix in place as the starting piece // NOTE that this is AFTER duplication
		}

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
		GetComponentInChildren<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
		enabled = false;
	}


	private static Tuple<Sprite, Bounds>[] VoronoiMasks(int count, float sizeWS, float pixelsPerUnit)
	{
		// pick random points in UV space
		System.Collections.Generic.List<Vector2> sites = new();
		for (int i = 0; i < count; i++)
		{
			sites.Add(new Vector2(UnityEngine.Random.Range(0.0f, 1.0f), UnityEngine.Random.Range(0.0f, 1.0f)));
		}

		// TODO: spread points via Voronoi relax?

		// determine closest site to each texel
		// TODO: efficiency?
		int sizeTexels = Mathf.RoundToInt(sizeWS * pixelsPerUnit);
		int[,] perTexelIndices = new int[sizeTexels, sizeTexels];
		for (int x = 0; x < sizeTexels; ++x)
		{
			for (int y = 0; y < sizeTexels; ++y)
			{
				float uvPerTexel = 1.0f / (sizeTexels - 1);
				Vector2 texelUV = new(x * uvPerTexel, y * uvPerTexel);
				float distSqMin = float.MaxValue;
				int nearestIdx = -1;
				int idx = 0;
				foreach (Vector2 site in sites)
				{
					float distSq = (site - texelUV).sqrMagnitude;
					if (distSq < distSqMin)
					{
						distSqMin = distSq;
						nearestIdx = idx;
					}
					++idx;
				}
				perTexelIndices[x, y] = nearestIdx;
			}
		}

		// TODO: enumerate adjacent Voronoi cells for m_snapPrecursors[]?

		// create textures/sprites
		Tuple<Sprite, Bounds>[] sprites = new Tuple<Sprite, Bounds>[count];
		for (int i = 0; i < sprites.Length; ++i)
		{
			Texture2D texture = new(sizeTexels, sizeTexels, TextureFormat.Alpha8, false) { name = "Voronoi texture" };
			float minX = float.MaxValue;
			float minY = float.MaxValue;
			float maxX = float.MinValue;
			float maxY = float.MinValue;
			for (int x = 0; x < sizeTexels; ++x)
			{
				for (int y = 0; y < sizeTexels; ++y)
				{
					if (perTexelIndices[x, y] == i)
					{
						texture.SetPixel(x, y, Color.white);
						minX = Mathf.Min(x, minX);
						minY = Mathf.Min(y, minY);
						maxX = Mathf.Max(x, maxX);
						maxY = Mathf.Max(y, maxY);
					}
					else
					{
						texture.SetPixel(x, y, Color.clear);
					}
				}
			}
			texture.Apply(false, true);
			sprites[i] = Tuple.Create(Sprite.Create(texture, new Rect(0, 0, sizeTexels, sizeTexels), Vector2.one * 0.5f), new Bounds(new Vector2(minX + maxX, minY + maxY) / sizeTexels - Vector2.one, new Vector2(maxX - minX, maxY - minY) / pixelsPerUnit)); // TODO: don't assume central pivot?
			sprites[i].Item1.name = "Voronoi sprite";
		}
		return sprites;
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

	private System.Collections.IEnumerator Follow()
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
