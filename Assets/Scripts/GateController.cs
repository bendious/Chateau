using System.Linq;
using UnityEngine;


public class GateController : MonoBehaviour, IUnlockable
{
	public WeightedObject<GameObject>[] m_lockPrefabs;

	public LockController m_lock;


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		GameObject lockPrefab = Utility.RandomWeighted(m_lockPrefabs);
		Vector3 spawnPos = lockRoom.InteriorPosition(true) + new Vector3(0.0f, -lockPrefab.GetComponentsInChildren<SpriteRenderer>().Min(renderer => renderer.bounds.min.y), 1.0f);
		m_lock = Instantiate(lockPrefab, spawnPos, Quaternion.identity).GetComponent<LockController>();
		m_lock.m_door = gameObject;

		m_lock.SpawnKeys(lockRoom, keyRooms);
	}
}
