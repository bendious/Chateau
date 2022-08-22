using System;
using System.Linq;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractFollow : MonoBehaviour, IInteractable, IKey
{
	public IUnlockable Lock { get; set; }


	[SerializeField] private WeightedObject<Sprite>[] m_spriteAlternates;

	[SerializeField] private InteractFollow[] m_snapPrecursors;
	[SerializeField] private float m_maxSnapDistance = 0.5f;
	[SerializeField] private float m_maxSnapDegrees = 45.0f;
	[SerializeField] private int m_splitCountMax;


	private RoomController m_room;

	private Vector3 m_correctPosition;
	private float m_correctRotationDegrees;

	private KinematicCharacter m_followCharacter;
	private Vector3 m_followOffset;
	private float m_followOffsetDegrees;


	public bool IsInPlace
	{
		get => m_followCharacter == null && (transform.position - m_correctPosition).magnitude < m_maxSnapDistance && transform.rotation.eulerAngles.z.FloatEqualDegrees(m_correctRotationDegrees, m_maxSnapDegrees);
		set { }
	}


	private void Awake()
	{
		m_room = GameController.Instance.RoomFromPosition(transform.position);

		SpriteRenderer rendererLocal = GetComponent<SpriteRenderer>();
		if (m_spriteAlternates != null && m_spriteAlternates.Length > 0)
		{
			rendererLocal.sprite = m_spriteAlternates.RandomWeighted();
			m_spriteAlternates = null; // NOTE that this is BEFORE duplication
		}

		if (m_splitCountMax > 0) // TODO: separate component?
		{
			// NOTE that this is BEFORE VoronoiMasks()
			rendererLocal.flipX = UnityEngine.Random.value < 0.5f;
			rendererLocal.flipY = UnityEngine.Random.value < 0.5f;

			Tuple<Sprite, Bounds>[] spritesAndBounds = VoronoiMasks(UnityEngine.Random.Range(2, m_splitCountMax + 1), rendererLocal); // TODO: influence number of pieces based on size / intended difficulty?
			m_splitCountMax = 0; // NOTE that this is BEFORE duplication
			foreach (Tuple<Sprite, Bounds> pair in spritesAndBounds)
			{
				// TODO: skip if no pixels are visible? update m_snapPrecursors
				InteractFollow newInteract = pair == spritesAndBounds.First() ? this : Instantiate(gameObject, transform.parent).GetComponent<InteractFollow>();
				newInteract.Lock = Lock;
				newInteract.GetComponent<SpriteMask>().sprite = pair.Item1;

				SpriteRenderer rendererChild = newInteract.GetComponent<SpriteRenderer>();
				rendererChild.flipX = rendererLocal.flipX;
				rendererChild.flipY = rendererLocal.flipY;

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

		// ensure we haven't expanded beyond room bounds due to sprite change/flip
		// TODO: more robust checks against room sides/top?
		transform.position += new Vector3(0.0f, Mathf.Max(0.0f, m_room.transform.position.y - rendererLocal.bounds.min.y));

		m_correctPosition = transform.position;
		m_correctRotationDegrees = transform.rotation.eulerAngles.z;
	}

	private void Start()
	{
		Quaternion rotation = Quaternion.Euler(0.0f, 0.0f, UnityEngine.Random.Range(0.0f, 360.0f));
		transform.SetPositionAndRotation(m_room.InteriorPosition((Lock as LockController).m_keyHeightMax, gameObject, rotation), rotation);
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
		return m_room == GameController.Instance.RoomFromPosition(interactor.transform.position);
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
			InteractFollow[] siblings = m_room.GetComponentsInChildren<InteractFollow>(); // TODO: work even between different rooms?
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


	public void SetCombination(LockController.CombinationSet set, int[] combination, int optionIndex, int indexCorrect, int startIndex, int endIndex, bool useSprites) => Debug.LogError("Can't SetCombination() on InteractFollow");

	public void Use() => Debug.LogError("Can't Use() an InteractFollow.");

	public void Deactivate()
	{
		transform.SetPositionAndRotation(m_correctPosition, Quaternion.Euler(0.0f, 0.0f, m_correctRotationDegrees)); // in case we were put within range w/o snapping
		m_followCharacter = null;
		GetComponentInChildren<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
		enabled = false;
	}


	private static Tuple<Sprite, Bounds>[] VoronoiMasks(int count, SpriteRenderer renderer)
	{
		// pick random points in UV space
		System.Collections.Generic.List<Vector2> sites = new();
		for (int i = 0; i < count; i++)
		{
			sites.Add(new(UnityEngine.Random.Range(0.0f, 1.0f), UnityEngine.Random.Range(0.0f, 1.0f)));
		}

		// TODO: spread points via Voronoi relax?

		// determine closest site to each texel
		// TODO: efficiency?
		Vector2Int sizeTexels = Vector2Int.RoundToInt(renderer.sprite.rect.size);
		Vector2 uvPerTexel = Vector2.one / (sizeTexels - Vector2Int.one);
		int[,] perTexelIndices = new int[sizeTexels.x, sizeTexels.y];
		for (int x = 0; x < sizeTexels.x; ++x)
		{
			for (int y = 0; y < sizeTexels.y; ++y)
			{
				Vector2 texelUV = new Vector2(x, y) * uvPerTexel;
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
			// create/fill texture
			Texture2D texture = new(sizeTexels.x, sizeTexels.y, TextureFormat.Alpha8, false) { name = "Voronoi texture" };
			float minX = float.MaxValue;
			float minY = float.MaxValue;
			float maxX = float.MinValue;
			float maxY = float.MinValue;
			for (int x = 0; x < sizeTexels.x; ++x)
			{
				for (int y = 0; y < sizeTexels.y; ++y)
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

			// create sprite
			Vector2 pivotUV = renderer.sprite.pivot / sizeTexels;
			if (renderer.flipX)
			{
				pivotUV.x = 1.0f - pivotUV.x;
			}
			if (renderer.flipY)
			{
				pivotUV.y = 1.0f - pivotUV.y;
			}
			float pixelsPerUnit = renderer.sprite.pixelsPerUnit;
			Vector2 offsetTexels = new Vector2(minX + maxX, minY + maxY) * 0.5f;
			Vector2 offsetUV = offsetTexels / sizeTexels - pivotUV;
			Vector2 offsetWS = offsetUV * sizeTexels / pixelsPerUnit;
			sprites[i] = Tuple.Create(Sprite.Create(texture, new(0, 0, sizeTexels.x, sizeTexels.y), pivotUV, pixelsPerUnit), new Bounds(offsetWS, new Vector2(maxX - minX, maxY - minY) / pixelsPerUnit));
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
		Bounds followBounds = m_room.BoundsInterior;

		while (m_followCharacter != null && followBounds.Contains(m_followCharacter.transform.position))
		{
			Tuple<Vector3, float> followTf = FollowTransform();
			transform.SetPositionAndRotation(followBounds.ClosestPoint(followTf.Item1 + Quaternion.Euler(0.0f, 0.0f, followTf.Item2) * m_followOffset), Quaternion.Euler(0.0f, 0.0f, followTf.Item2 + m_followOffsetDegrees)); // TODO: limit entire bounding box rather than just the pivot point to followBounds
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
