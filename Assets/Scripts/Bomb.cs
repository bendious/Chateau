using UnityEngine;


public class Bomb : MonoBehaviour
{
	public GameObject m_explosion;


	private void Start()
	{
		ObjectDespawn.OnExecute += OnDespawn;
	}

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
		ObjectDespawn.OnExecute -= OnDespawn;
	}


	private void ProcessCollision(Collider2D collider)
	{
		if (collider.GetComponent<EnemyController>() == null)
		{
			return;
		}

		gameObject.SetActive(false);
		Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
	}

	private void OnDespawn(ObjectDespawn evt)
	{
		if (evt.m_object != gameObject)
		{
			return;
		}

		Explosion explosion = Instantiate(m_explosion, transform.position, transform.rotation).GetComponent<Explosion>();
		explosion.m_source = GetComponent<ItemController>().Cause;
	}
}
