using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


public class RoomController : MonoBehaviour
{
	public GameObject m_roomPrefab;
	public GameObject m_doorPrefab;
	public GameObject m_tablePrefab;

	public GameObject m_doorL;
	public GameObject m_doorR;
	public GameObject m_doorB;
	public GameObject m_doorT;

	public GameObject[] m_ladderPieces;

	public float m_roomSpawnPct = 0.5f;
	public int m_spawnDepthMax = 5;

	public int m_tablesMin = 0;
	public int m_tablesMax = 2;


	private bool m_leftConnected = false;
	private bool m_rightConnected = false;
	private bool m_bottomConnected = false;
	private bool m_topConnected = false;

	private /*readonly*/ RoomController m_leftChild;
	private /*readonly*/ RoomController m_rightChild;
	private /*readonly*/ RoomController m_bottomChild;
	private /*readonly*/ RoomController m_topChild;

	private /*readonly*/ GameObject m_leftLock;
	private /*readonly*/ GameObject m_rightLock;
	private /*readonly*/ GameObject m_bottomLock;
	private /*readonly*/ GameObject m_topLock;

	private bool m_childrenCreated = false;


	private readonly Color m_oneWayPlatformColor = new Color(0.3f, 0.2f, 0.1f);


	private void Start()
	{
		// calculate size info
		Bounds bounds = CalculateBounds();
		Vector3 offsetMagH = new Vector3(bounds.size.x, 0.0f, 0.0f);
		Vector3 offsetMagV = new Vector3(0.0f, bounds.size.y, 0.0f);
		Vector3 checkSize = bounds.size - new Vector3(0.1f, 0.1f, 0.0f); // NOTE the small reduction to avoid always collecting ourself

		// replace doors / spawn rooms
		// TODO: randomize order to avoid directional bias?
		m_leftChild = MaybeReplaceDoor(ref m_leftConnected, bounds, -offsetMagH, checkSize, ref m_leftLock, m_doorL, null, child => child.m_rightConnected = true);
		m_rightChild = MaybeReplaceDoor(ref m_rightConnected, bounds, offsetMagH, checkSize, ref m_rightLock, m_doorR, null, child => child.m_leftConnected = true);
		m_bottomChild = MaybeReplaceDoor(ref m_bottomConnected, bounds, -offsetMagV, checkSize, ref m_bottomLock, m_doorB, null, child => child.m_topConnected = true);
		m_topChild = MaybeReplaceDoor(ref m_topConnected, bounds, offsetMagV, checkSize, ref m_topLock, m_doorT, m_ladderPieces, child => child.m_bottomConnected = true);

		m_childrenCreated = true;

		// spawn tables
		// TODO: more deliberate spawning
		int tableCount = UnityEngine.Random.Range(m_tablesMin, m_tablesMax + 1);
		float extentX = offsetMagH.x * 0.5f;
		BoxCollider2D tableCollider = m_tablePrefab.GetComponent<BoxCollider2D>();
		float tableExtentY = tableCollider.size.y * 0.5f + tableCollider.edgeRadius - tableCollider.offset.y;
		GameObject newTable = null;
		for (int i = 0; i < tableCount; ++i)
		{
			if (newTable == null)
			{
				newTable = Instantiate(m_tablePrefab); // NOTE that we have to spawn before placement due to size randomization in Awake()
			}
			Bounds newBounds = newTable.GetComponent<Collider2D>().bounds;
			Vector3 spawnPos = transform.position + new Vector3(UnityEngine.Random.Range(-extentX + newBounds.extents.x, extentX - newBounds.extents.x), tableExtentY, 0.0f);
			if (Physics2D.OverlapBox(spawnPos + newBounds.center + new Vector3(0.0f, 0.1f, 0.0f), newBounds.size, 0.0f) != null) // NOTE the small offset to avoid collecting the floor; also that this will collect our newly spawned table when at the origin, but that's okay since keeping the start point clear isn't objectionable
			{
				continue; // re-place and try again
			}
			newTable.transform.position = spawnPos;
			newTable = null;
		}
		if (newTable != null)
		{
			Destroy(newTable);
		}
	}


	public bool AllChildrenReady()
	{
		if (!m_childrenCreated)
		{
			return false;
		}
		return (new RoomController[] { m_leftChild, m_rightChild, m_bottomChild, m_topChild }).All(child => child == null || child.AllChildrenReady());
	}

	public Vector3 ChildFloorPosition(bool checkLocks, GameObject targetObj)
	{
		// enumerate valid options
		RoomController[] options = (new Tuple<RoomController, GameObject>[] { new Tuple<RoomController, GameObject>(this, null), Tuple.Create(m_leftChild, m_leftLock), Tuple.Create(m_rightChild, m_rightLock), Tuple.Create(m_bottomChild, m_bottomLock), Tuple.Create(m_topChild, m_topLock) }).Where(child => child.Item1 != null && child.Item1.m_childrenCreated && (!checkLocks || child.Item2 == null)).Select(pair => pair.Item1).ToArray();

		// weight options based on distance to target
		float[] optionWeights = targetObj == null ? Enumerable.Repeat(1.0f, options.Length).ToArray() : options.Select(option => 1.0f / (option.transform.position - targetObj.transform.position).magnitude).ToArray();
		RoomController child = options.Length == 0 ? this : Utility.RandomWeighted(options, optionWeights);

		if (child == this)
		{
			// return interior position
			// TODO: avoid spawning right on top of targetObj
			float xDiffMax = CalculateBounds().extents.x - 0.5f; // TODO: determine floor/wall extent automatically
			return transform.position + Vector3.right * UnityEngine.Random.Range(-xDiffMax, xDiffMax);
		}

		// return position from child room
		return child.ChildFloorPosition(checkLocks, targetObj);
	}

	public Tuple<RoomController, int> LeafRoomFarthest(int startDistance = 0)
	{
		// enumerate non-null children
		Assert.IsTrue(m_childrenCreated);
		Tuple<RoomController, int>[] childrenPreprocess = (new Tuple<RoomController, int>[] { Tuple.Create(m_leftChild, m_leftLock != null ? 1 : 0), Tuple.Create(m_rightChild, m_rightLock != null ? 1 : 0), Tuple.Create(m_bottomChild, m_bottomLock != null ? 1 : 0), Tuple.Create(m_topChild, m_topLock != null ? 1 : 0) }).Where(child => child.Item1 != null).ToArray();
		if (childrenPreprocess.Length == 0)
		{
			return Tuple.Create(this, startDistance);
		}

		// recursively find farthest distance
		Tuple<RoomController, int>[] childrenPostprocessed = childrenPreprocess.Select(child => child.Item1.LeafRoomFarthest(child.Item2)).ToArray();
		int maxDistance = childrenPostprocessed.Max(a => a.Item2);

		// pick random child at farthest distance
		Tuple<RoomController, int>[] childrenMax = childrenPostprocessed.Where(childTuple => childTuple.Item2 >= maxDistance).ToArray();
		return Tuple.Create(childrenMax[UnityEngine.Random.Range(0, childrenMax.Length)].Item1, maxDistance + 1);
	}


	// see https://gamedev.stackexchange.com/questions/86863/calculating-the-bounding-box-of-a-game-object-based-on-its-children
	private Bounds CalculateBounds()
	{
		Renderer[] renderers = GetComponentsInChildren<Renderer>();
		if (renderers.Length == 0)
		{
			return new Bounds(transform.position, Vector3.zero);
		}
		Bounds b = renderers[0].bounds;
		foreach (Renderer r in renderers)
		{
			b.Encapsulate(r.bounds);
		}
		return b;
	}

	private RoomController MaybeReplaceDoor(ref bool isOpen, Bounds bounds, Vector3 replaceOffset, Vector3 checkSize, ref GameObject lockObj, GameObject door, GameObject[] ladderPieces, Action<RoomController> postReplace)
	{
		bool spawnedFromThisDirection = isOpen;
		bool canSpawnRoom = !spawnedFromThisDirection && m_spawnDepthMax > 0 && Physics2D.OverlapBox(bounds.center + replaceOffset, checkSize, 0.0f) == null;
		Assert.IsTrue(!spawnedFromThisDirection || !canSpawnRoom);
		isOpen = spawnedFromThisDirection || (canSpawnRoom && UnityEngine.Random.value > m_roomSpawnPct);

		if (!isOpen)
		{
			if (ladderPieces != null)
			{
				foreach (GameObject piece in ladderPieces)
				{
					Destroy(piece);
				}
			}

			return null;
		}

		if (!spawnedFromThisDirection && UnityEngine.Random.value > 0.95f/*TODO*/)
		{
			// create locked door
			lockObj = Instantiate(m_doorPrefab, door.transform.position, Quaternion.identity);
			Vector2 size = door.GetComponent<BoxCollider2D>().size * door.transform.localScale;
			lockObj.GetComponent<BoxCollider2D>().size = size;
			lockObj.GetComponent<SpriteRenderer>().size = size;
		}

		// enable one-way movement or destroy
		PlatformEffector2D effector = door.GetComponent<PlatformEffector2D>();
		if (effector == null)
		{
			Destroy(door);
		}
		else
		{
			// enable effector for dynamic collisions
			effector.enabled = true;
			door.GetComponent<Collider2D>().usedByEffector = true;

			// set layer for kinematic movement
			door.layer = LayerMask.NameToLayer("OneWayPlatforms");

			// change color/shadows for user visibility
			door.GetComponent<SpriteRenderer>().color = m_oneWayPlatformColor;
			Destroy(door.GetComponent<UnityEngine.Rendering.Universal.ShadowCaster2D>());
		}

		if (!canSpawnRoom)
		{
			return null;
		}

		RoomController newRoom = Instantiate(m_roomPrefab, transform.position + replaceOffset, Quaternion.identity).GetComponent<RoomController>();
		newRoom.m_roomPrefab = m_roomPrefab; // NOTE that since Unity's method of internal prefab references doesn't allow a script to reference the prefab that contains it, we have to manually update the child's reference here
		newRoom.m_spawnDepthMax = m_spawnDepthMax - 1;
		postReplace(newRoom);
		return newRoom;
	}
}
