using System.Linq;
using UnityEngine;


public class Hazard : MonoBehaviour
{
	[SerializeField] private float m_damage = 1.0f;
	[SerializeField] private Health.DamageType m_type;

	[SerializeField] private Vector2 m_damageImpulse;
	[SerializeField] private WeightedObject<AudioClip>[] m_damageSfx;

	[SerializeField] private bool m_singleUse;
	[SerializeField] private bool m_reenableOnDamage;
	[SerializeField] private float m_lifetimeSeconds = -1.0f;

	[SerializeField] private UnityEngine.VFX.VisualEffect m_vfx;
	[SerializeField] private WeightedObject<AudioClip>[] m_enableSfx;


	private bool m_hasTrigger;

	private bool m_softstopping = false;


	private void Start() => m_hasTrigger = GetComponents<Collider2D>().Any(collider => collider.isTrigger);

	private void OnEnable()
	{
		EnableEffects();
		if (m_reenableOnDamage)
		{
			OnHealthDecrement.OnExecute += OnHealthDecremented;
		}
	}

	private void OnDisable()
	{
		if (m_reenableOnDamage)
		{
			OnHealthDecrement.OnExecute -= OnHealthDecremented;
		}
	}

	private void OnTriggerEnter2D(Collider2D collider) => ApplyDamage(null, collider);

	private void OnTriggerStay2D(Collider2D collider) => ApplyDamage(null, collider);

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


	private void EnableEffects()
	{
		if (m_vfx != null)
		{
			m_vfx.gameObject.SetActive(true);
			m_vfx.enabled = true;
			m_vfx.Play();
		}
		if (m_enableSfx.Length > 0)
		{
			GetComponent<AudioSource>().PlayOneShot(m_enableSfx.RandomWeighted());
		}
		m_softstopping = false;

		if (m_lifetimeSeconds >= 0.0f)
		{
			StartCoroutine(DisableSoftly(m_lifetimeSeconds));
		}
	}

	private void ApplyDamage(Collision2D collision, Collider2D collider)
	{
		if (!enabled || m_softstopping)
		{
			return;
		}

		// TODO: check for unlocking

		if (!collider.TryGetComponent(out Health health))
		{
			return;
		}
		health.Decrement(gameObject, gameObject, m_damage, m_type);

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
			StartCoroutine(DisableSoftly(0.0f));
		}
	}

	private void OnHealthDecremented(OnHealthDecrement evt)
	{
		if (evt.m_health.gameObject != gameObject)
		{
			return;
		}
		EnableEffects();
	}

	private System.Collections.IEnumerator DisableSoftly(float delaySeconds)
	{
		if (delaySeconds > 0.0f)
		{
			yield return new WaitForSeconds(delaySeconds);
		}

		m_softstopping = true;
		if (m_vfx != null)
		{
			yield return StartCoroutine(m_vfx.gameObject.SoftStop(() => !m_softstopping));
		}
		if (m_softstopping && !m_reenableOnDamage) // NOTE that we can't disable ourself completely if we need to continue listening for damage events
		{
			enabled = false;
			m_softstopping = false;
		}
	}
}
