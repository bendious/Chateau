using System.Linq;
using UnityEngine;


public class GateController : MonoBehaviour, IUnlockable
{
	public WeightedObject<GameObject>[] m_lockPrefabs;

	public LockController m_lock;


	public void SpawnKeys(RoomController lockRoom, RoomController keyRoom)
	{
		GameObject lockPrefab = Utility.RandomWeighted(m_lockPrefabs);
		Vector3 spawnPos = /*lock*/keyRoom.ChildPosition(false, null, true, false) + new Vector3(0.0f, -lockPrefab.GetComponentsInChildren<SpriteRenderer>().Min(renderer => renderer.bounds.min.y), 1.0f); // TODO: put lock in lockRoom once gates are placed correctly
		m_lock = Instantiate(lockPrefab, spawnPos, Quaternion.identity).GetComponent<LockController>();
		m_lock.m_door = gameObject;

		m_lock.SpawnKeys(lockRoom, keyRoom);
	}
}
