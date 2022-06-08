using UnityEngine;


[DisallowMultipleComponent]
public class DespawnEffect : MonoBehaviour
{
	[SerializeField] private GameObject m_prefab;

	[SerializeField] private bool m_enemyAutoTrigger; // TODO: split into separate script?


	private bool m_isQuitting = false;


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
		if (m_isQuitting || GameController.IsReloading)
		{
			return;
		}

		Explosion explosion = Instantiate(m_prefab, transform.position, transform.rotation).GetComponent<Explosion>();
		if (explosion != null)
		{
			explosion.m_source = GetComponent<ItemController>().Cause;
		}
	}

	private void OnApplicationQuit()
	{
		m_isQuitting = true;
	}


	private void ProcessCollision(Collider2D collider)
	{
		if (!m_enemyAutoTrigger || collider.GetComponent<EnemyController>() == null || GetComponent<ItemController>().Cause == null)
		{
			return;
		}

		gameObject.SetActive(false);
		Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
	}
}
