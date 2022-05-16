using UnityEngine;


public class Hazard : MonoBehaviour
{
	[SerializeField]
	private float m_damage = 1.0f;


	private void OnCollisionEnter2D(Collision2D collision)
	{
		ApplyDamage(collision);
	}

	private void OnCollisionStay2D(Collision2D collision)
	{
		ApplyDamage(collision);
	}


	private void ApplyDamage(Collision2D collision)
	{
		// TODO: check for unlocking

		Health health = collision.gameObject.GetComponent<Health>();
		if (health == null)
		{
			return;
		}
		health.Decrement(gameObject, m_damage);
	}
}
