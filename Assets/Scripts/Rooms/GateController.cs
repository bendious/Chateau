using System.Linq;
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

	public WeightedObject<GameObject>[] m_ladderPrefabs;


	public GameObject Parent { get; set; }


	private IUnlockable m_child;


	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (!IsValidNextKey(collider.gameObject))
		{
			return;
		}

		// TODO: check for full unlocking first?
		Parent.GetComponent<RoomController>().SpawnLadder(gameObject, m_ladderPrefabs?.RandomWeighted(), true);
		IKey key = collider.GetComponent<IKey>();
		key.Use();
		m_child.Unlock(key);
	}

	public void SpawnKeysStatic(RoomController lockRoom, RoomController[] keyRooms)
	{
		Debug.Assert(m_child == null);

		// determine lock type
		int keyRoomsCount = keyRooms == null ? 0 : keyRooms.Length;
		LockInfo lockInfo = RoomController.RandomWeightedByKeyCount(m_lockPrefabs, info =>
		{
			WeightedObject<LockController.KeyInfo>[] keys = info.m_prefab.GetComponent<LockController>().m_keyPrefabs;
			return keys == null || keys.Length == 0 ? keyRoomsCount : keys.Min(key => key.m_object.m_keyCountMax - keyRoomsCount < 0 ? int.MaxValue : key.m_object.m_keyCountMax - keyRoomsCount);
		});

		float yOffset = lockInfo.m_prefab.OriginToCenterY(true).y;
		Vector3 spawnPos = lockRoom.InteriorPosition(lockInfo.m_heightMin + yOffset, lockInfo.m_heightMax + yOffset, lockInfo.m_prefab); // TODO: prioritize placing near self if multiple gates in this room?
		if (lockInfo.m_alignVertically)
		{
			spawnPos.x = transform.position.x;
		}

		m_child = Instantiate(lockInfo.m_prefab, spawnPos, Quaternion.identity, transform.parent).GetComponent<IUnlockable>();
		m_child.Parent = gameObject;
		m_child.SpawnKeysStatic(lockRoom, keyRooms);
	}

	public void SpawnKeysDynamic(RoomController lockRoom, RoomController[] keyRooms) => m_child?.SpawnKeysDynamic(lockRoom, keyRooms);

	public bool IsValidNextKey(GameObject obj)
	{
		return m_child != null && m_child.IsValidNextKey(obj);
	}

	public bool Unlock(IKey key)
	{
		GameController.Instance.AddCameraTargets(transform);
		Simulation.Schedule<ObjectDespawn>(1.0f).m_object = gameObject; // TODO: guarantee camera reaches us?
		return true;
	}
}
