using UnityEngine;


[DisallowMultipleComponent]
public class DespawnEffect : MonoBehaviour
{
	[SerializeField] private GameObject m_prefab;

	[SerializeField] private bool m_enemyAutoTrigger; // TODO: split into separate script?


	private void OnTriggerEnter2D(Collider2D collider)
	{
		ProcessCollision(collider);
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		ProcessCollision(collision.collider);
	}

	private void OnDestroy()
	{
		if (GameController.IsSceneLoad || gameObject.activeSelf) // NOTE that if the game object is still active, then this script is getting removed while the object is still alive, so we shouldn't activate despawn effects
		{
			return;
		}

		Explosion explosion = Instantiate(m_prefab, transform.position, transform.rotation).GetComponent<Explosion>();
		if (explosion != null)
		{
			explosion.m_source = GetComponent<ItemController>().Cause;
		}
	}


	private void ProcessCollision(Collider2D collider)
	{
		if (!m_enemyAutoTrigger || GetComponent<ItemController>().Cause == null)
		{
			return;
		}

		AIController ai = collider.GetComponent<AIController>();
		if (ai == null || ai.m_friendly)
		{
			return;
		}

		gameObject.SetActive(false);
		Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
	}
}
