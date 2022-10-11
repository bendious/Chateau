using System.Linq;
using UnityEngine;


public class Hazard : MonoBehaviour
{
	[SerializeField] private float m_damage = 1.0f;
	[SerializeField] private Health.DamageType m_type;

	[SerializeField] private Vector2 m_damageImpulse;
	[SerializeField] private WeightedObject<AudioClip>[] m_damageSfx;

	[SerializeField] private bool m_singleUse;


	private bool m_hasTrigger;


	private void Start()
	{
		// NOTE that this, even if empty, ensures the enable/disable checkbox is shown in the Inspector, which we want since we manually check our enabled state in ApplyDamage()

		m_hasTrigger = GetComponents<Collider2D>().Any(collider => collider.isTrigger);
	}

	private void OnTriggerEnter2D(Collider2D collider)
	{
		ApplyDamage(null, collider);
	}

	private void OnTriggerStay2D(Collider2D collider)
	{
		ApplyDamage(null, collider);
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		if (m_hasTrigger)
		{
			return;
		}
		ApplyDamage(collision, collision.collider);
	}

	private void OnCollisionStay2D(Collision2D collision)
	{
		if (m_hasTrigger)
		{
			return;
		}
		ApplyDamage(collision, collision.collider);
	}


	private void ApplyDamage(Collision2D collision, Collider2D collider)
	{
		if (!enabled)
		{
			return;
		}

		// TODO: check for unlocking

		Health health = collider.GetComponent<Health>();
		if (health == null)
		{
			return;
		}
		health.Decrement(gameObject, m_damage, m_type);

		if (m_damageImpulse != Vector2.zero)
		{
			Vector2 contactPos = collision == null ? collider.transform.position : collision.GetContact(0).point; // TODO: handle multiple contact points?
			GetComponent<Rigidbody2D>().AddForceAtPosition(m_damageImpulse, 2.0f * (Vector2)transform.position - contactPos);
		}

		// SFX
		if (m_damageSfx.Length > 0)
		{
			GetComponent<AudioSource>().PlayOneShot(m_damageSfx.RandomWeighted());
		}

		if (m_singleUse)
		{
			enabled = false;
		}
	}
}
