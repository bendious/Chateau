using UnityEngine;


[DisallowMultipleComponent]
public class GateController : MonoBehaviour, IUnlockable
{
	[System.Serializable]
	public class LockInfo
	{
		public GameObject m_prefab;
		public float m_heightMin;
		public float m_heightMax;
		[SerializeField] internal bool m_alignVertically;
	}
	public WeightedObject<LockInfo>[] m_lockPrefabs;

	public WeightedObject<RoomController.LadderInfo>[] m_ladderPrefabs;


	public GameObject Parent { get; set; }
	public bool IsLocked => m_child != null && m_child.IsLocked;

	public bool IsCriticalPath { private get; set; }


	private IUnlockable m_child;


	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (!IsValidNextKey(collider.gameObject))
		{
			return;
		}

		// TODO: check for full unlocking first?
		IKey key = collider.GetComponent<IKey>();
		m_child.Unlock(key); // NOTE that this has to be BEFORE key.Use() since that might detach the item and lose the cause before it has been verified
		key.Use();
	}

	public void SpawnKeysStatic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct)
	{
		Debug.Assert(m_child == null);

		// determine lock type
		int keyRoomsCount = keyRooms == null ? 0 : keyRooms.Length;
		LockInfo lockInfo = RoomController.RandomWeightedByKeyCount(m_lockPrefabs.CombineWeighted(GameController.Instance.m_lockPrefabs, info => info.m_object.m_prefab, pair => pair.m_object), (info, preferredKeyCount) => RoomController.ObjectToKeyStats(info.m_prefab, preferredKeyCount), keyRoomsCount, difficultyPct);

		float yOffset = lockInfo.m_prefab.OriginToCenterY(true).y;
		Vector3 spawnPos = lockRoom.InteriorPosition(lockInfo.m_heightMin + yOffset, lockInfo.m_heightMax + yOffset, lockInfo.m_prefab, xPreferred: transform.position.x);
		if (lockInfo.m_alignVertically)
		{
			spawnPos.x = transform.position.x; // TODO: move other object(s) if overlapping?
		}

		m_child = Instantiate(lockInfo.m_prefab, spawnPos, Quaternion.identity, lockRoom.transform).GetComponent<IUnlockable>();
		m_child.Parent = gameObject;
		m_child.IsCriticalPath = IsCriticalPath;
		m_child.SpawnKeysStatic(lockRoom, keyRooms, difficultyPct);
	}

	public void SpawnKeysDynamic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct) => m_child?.SpawnKeysDynamic(lockRoom, keyRooms, difficultyPct);

	public bool IsValidNextKey(GameObject obj)
	{
		return m_child != null && m_child.IsValidNextKey(obj);
	}

	public bool Unlock(IKey key, bool silent = false)
	{
		if (m_ladderPrefabs != null && m_ladderPrefabs.Length > 0)
		{
			Parent.GetComponent<RoomController>().SpawnLadder(gameObject, m_ladderPrefabs.RandomWeighted(), true);
			Simulation.Schedule<CameraTargetRemove>(1.0f).m_transform = transform; // TODO: parameterize? guarantee camera reaches us?
		}
		else
		{
			StartCoroutine(this.GateDespawnAnimationCoroutine());
		}

		if (!silent)
		{
			GameController.Instance.AddCameraTargets(transform);
		}
		return true;
	}
}
