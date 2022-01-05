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

	public Color m_oneWayPlatformColor = new Color(0.3f, 0.2f, 0.1f);

	public float m_roomSpawnPct = 0.5f;
	public int m_spawnDepthMax = 5;

	public int m_tablesMin = 0;
	public int m_tablesMax = 2;


	private bool m_leftOpen = false;
	private bool m_rightOpen = false;
	private bool m_bottomOpen = false;
	private bool m_topOpen = false;

	private /*readonly*/ RoomController m_leftChild;
	private /*readonly*/ RoomController m_rightChild;
	private /*readonly*/ RoomController m_bottomChild;
	private /*readonly*/ RoomController m_topChild;

	private bool m_childrenCreated = false;


	private void Start()
	{
		// calculate size info
		Bounds bounds = CalculateBounds();
		Vector3 offsetMagH = new Vector3(bounds.size.x, 0.0f, 0.0f);
		Vector3 offsetMagV = new Vector3(0.0f, bounds.size.y, 0.0f);
		Vector3 checkSize = bounds.size - new Vector3(0.1f, 0.1f, 0.0f); // NOTE the small reduction to avoid always collecting ourself

		// replace doors / spawn rooms
		// TODO: randomize order to avoid directional bias?
		m_leftChild = MaybeReplaceDoor(ref m_leftOpen, bounds, -offsetMagH, checkSize, m_doorL, child => child.m_rightOpen = true);
		m_rightChild = MaybeReplaceDoor(ref m_rightOpen, bounds, offsetMagH, checkSize, m_doorR, child => child.m_leftOpen = true);
		m_bottomChild = MaybeReplaceDoor(ref m_bottomOpen, bounds, -offsetMagV, checkSize, m_doorB, child => child.m_topOpen = true);
		m_topChild = MaybeReplaceDoor(ref m_topOpen, bounds, offsetMagV, checkSize, m_doorT, child => child.m_bottomOpen = true);

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

	public Vector3 ChildFloorPosition()
	{
		// enumerate non-null children
		RoomController[] children = (new RoomController[] { m_leftChild, m_rightChild, m_bottomChild, m_topChild }).Where(child => child != null && child.m_childrenCreated).ToArray();
		if (!m_childrenCreated || children.Length == 0)
		{
			// return interior position
			float xDiffMax = CalculateBounds().extents.x - 0.5f; // TODO: determine floor/wall extent automatically
			return transform.position + Vector3.right * UnityEngine.Random.Range(-xDiffMax, xDiffMax);
		}

		// return position from child room
		return children[UnityEngine.Random.Range(0, children.Length)].ChildFloorPosition();
	}

	public Tuple<RoomController, int> LeafRoomFarthest()
	{
		// enumerate non-null children
		Assert.IsTrue(m_childrenCreated);
		RoomController[] children = (new RoomController[] { m_leftChild, m_rightChild, m_bottomChild, m_topChild }).Where(child => child != null).ToArray();
		if (children.Length == 0)
		{
			return Tuple.Create(this, 0);
		}

		// recursively find farthest distance
		Tuple<RoomController, int>[] childTuples = children.Select(child => child.LeafRoomFarthest()).ToArray();
		int maxDistance = childTuples.Max(a => a.Item2);

		// pick random child at farthest distance
		Tuple<RoomController, int>[] childTuplesMax = childTuples.Where(childTuple => childTuple.Item2 >= maxDistance).ToArray();
		return Tuple.Create(childTuplesMax[UnityEngine.Random.Range(0, childTuplesMax.Length)].Item1, maxDistance + 1);
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

	private RoomController MaybeReplaceDoor(ref bool isOpen, Bounds bounds, Vector3 replaceOffset, Vector3 checkSize, GameObject door, Action<RoomController> postReplace)
	{
		bool spawnedFromThisDirection = isOpen;
		bool canSpawnRoom = !spawnedFromThisDirection && m_spawnDepthMax > 0 && Physics2D.OverlapBox(bounds.center + replaceOffset, checkSize, 0.0f) == null;
		Assert.IsTrue(!spawnedFromThisDirection || !canSpawnRoom);
		isOpen = spawnedFromThisDirection || (canSpawnRoom && UnityEngine.Random.value > m_roomSpawnPct);

		if (!isOpen)
		{
			return null;
		}

		if (UnityEngine.Random.value > 0.95f/*TODO*/)
		{
			// create locked door
			GameObject newDoor = Instantiate(m_doorPrefab, door.transform.position, Quaternion.identity);
			Vector2 size = door.GetComponent<BoxCollider2D>().size;
			newDoor.GetComponent<BoxCollider2D>().size = size;
			newDoor.GetComponent<SpriteRenderer>().size = size;
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
