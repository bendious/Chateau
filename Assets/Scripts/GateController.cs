using System.Linq;
using UnityEngine;


public class GateController : MonoBehaviour, IUnlockable
{
	public WeightedObject<GameObject>[] m_lockPrefabs;

	public WeightedObject<GameObject>[] m_ladderPrefabs;


	public GameObject Parent { get; set; }


	private IUnlockable m_child;


	private void OnTriggerEnter2D(Collider2D collider)
	{
		if (!IsKey(collider.gameObject))
		{
			return;
		}

		Parent.GetComponent<RoomController>().SpawnLadder(gameObject, m_ladderPrefabs?.RandomWeighted(), true);
		m_child.Unlock(collider.gameObject);
	}

	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		GameObject lockPrefab = m_lockPrefabs.RandomWeighted();
		float height = -lockPrefab.GetComponentsInChildren<SpriteRenderer>().Min(renderer => renderer.bounds.min.y);
		Vector3 spawnPos = lockRoom.InteriorPosition(height, height, lockPrefab);

		m_child = Instantiate(lockPrefab, spawnPos, Quaternion.identity, transform.parent).GetComponent<IUnlockable>();
		m_child.Parent = gameObject;
		m_child.SpawnKeys(lockRoom, keyRooms);
	}

	public bool IsKey(GameObject obj)
	{
		return m_child.IsKey(obj);
	}

	public bool Unlock(GameObject key)
	{
		GameController.Instance.AddCameraTargets(transform);
		Simulation.Schedule<ObjectDespawn>(1.0f).m_object = gameObject; // TODO: guarantee camera reaches us?
		return true;
	}
}
