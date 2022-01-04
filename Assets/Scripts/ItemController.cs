using Platformer.Core;
using Platformer.Gameplay;
using Platformer.Mechanics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.VFX;


[RequireComponent(typeof(Rigidbody2D), typeof(AudioSource))]
public class ItemController : MonoBehaviour
{
	public float m_swingDegreesPerSec = 5000.0f;
	public float m_swingRadiusPerSec = 10.0f;
	public float m_aimSpringStiffness = 100.0f;
	public float m_aimSpringDampPct = 0.5f;
	public float m_radiusSpringStiffness = 25.0f;
	public float m_radiusSpringDampPct = 0.5f;
	public float m_damageThresholdSpeed = 2.0f;
	public float m_throwSpeed = 100.0f;

	public AudioClip[] m_swingThrowAudio;
	public AudioClip[] m_collisionAudio;


	public bool LeftFacing => Mathf.Cos(Mathf.Deg2Rad * m_aimDegrees) < 0.0f; // TODO: efficiency?


	private float m_aimDegrees;
	private float m_aimVelocity;
	private float m_aimRadius;
	private float m_aimRadiusVelocity;
	private bool m_swingDirection;

	private Rigidbody2D m_body;
	private VisualEffect m_vfx;
	private AudioSource m_audioSource;
	private Collider2D m_collider;
	private SpriteRenderer m_renderer;
	private Health m_health;


	private void Awake()
	{
		m_body = GetComponent<Rigidbody2D>();
		m_vfx = GetComponent<VisualEffect>();
		m_audioSource = GetComponent<AudioSource>();
		m_collider = GetComponent<Collider2D>();
		m_renderer = GetComponent<SpriteRenderer>();
		m_health = GetComponent<Health>();
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		ProcessCollision(collision);
	}

	private void OnCollisionStay2D(Collision2D collision)
	{
		ProcessCollision(collision);
	}


	public void AttachTo(GameObject obj)
	{
		transform.SetParent(obj.transform);
		m_body.bodyType = RigidbodyType2D.Kinematic;
		m_aimDegrees = AimDegreesRaw(transform.position);
		m_aimVelocity = 0.0f;
		m_aimRadiusVelocity = 0.0f;
	}

	public void Detach()
	{
		transform.SetParent(null);
		transform.position = (Vector2)transform.position; // nullify any z that may have been applied for rendering order
		m_body.bodyType = RigidbodyType2D.Dynamic;
		m_aimVelocity = 0.0f;
		m_aimRadiusVelocity = 0.0f;
	}

	public void Swing()
	{
		m_aimVelocity += m_swingDirection ? m_swingDegreesPerSec : -m_swingDegreesPerSec;
		m_aimRadiusVelocity += m_swingRadiusPerSec;
		m_swingDirection = !m_swingDirection;

		m_vfx.enabled = true;
		Simulation.Schedule<DisableVFX>(0.25f).m_vfx = m_vfx;

		// play audio
		if (m_swingThrowAudio != null && m_swingThrowAudio.Length > 0)
		{
			m_audioSource.PlayOneShot(m_swingThrowAudio[Random.Range(0, m_swingThrowAudio.Length)]);
		}
	}

	public void UpdateAim(Vector3 position, float radius)
	{
		Assert.IsFalse(position.x == float.MaxValue || position.x == float.PositiveInfinity || position.x == float.NegativeInfinity || position.x == float.NaN || radius == float.MaxValue || radius == float.PositiveInfinity || radius == float.NegativeInfinity || radius == float.NaN); // TODO: prevent unbounded radius growth (caused by low framerate?)
		m_aimDegrees = DampedSpring(m_aimDegrees, AimDegreesRaw(position), m_aimSpringDampPct, true, m_aimSpringStiffness, ref m_aimVelocity);
		m_aimRadius = DampedSpring(m_aimRadius, radius, m_radiusSpringDampPct, false, m_radiusSpringStiffness, ref m_aimRadiusVelocity);

		transform.localRotation = Quaternion.Euler(0.0f, 0.0f, m_aimDegrees);
		transform.localPosition = transform.localRotation * Vector3.right * m_aimRadius - Vector3.forward; // NOTE the negative Z in order to force rendering on top of our parent
		m_renderer.flipY = LeftFacing;
	}

	public void Throw()
	{
		// temporarily ignore collisions w/ thrower
		Collider2D parentCollider = transform.parent.GetComponent<Collider2D>();
		Physics2D.IgnoreCollision(m_collider, parentCollider, true);
		EnableCollision evt = Simulation.Schedule<EnableCollision>(0.1f);
		evt.m_collider1 = m_collider;
		evt.m_collider2 = parentCollider;

		Detach();
		m_body.AddForce(Quaternion.Euler(0.0f, 0.0f, m_aimDegrees) * Vector2.right * m_throwSpeed);

		m_vfx.enabled = true;
		Simulation.Schedule<DisableVFX>(0.5f).m_vfx = m_vfx;

		// play audio
		if (m_swingThrowAudio != null && m_swingThrowAudio.Length > 0)
		{
			m_audioSource.PlayOneShot(m_swingThrowAudio[Random.Range(0, m_swingThrowAudio.Length)]);
		}
	}


	private void ProcessCollision(Collision2D collision)
	{
		// TODO: flag/unflag within the physics system itself?
		KinematicObject kinematicObj = collision.gameObject.GetComponent<KinematicObject>();
		if (kinematicObj != null && kinematicObj.ShouldIgnore(m_body, m_collider, true, false))
		{
			return;
		}

		// maybe attach to character
		AnimationController character = collision.gameObject.GetComponent<AnimationController>();
		if (character != null && character.IsPickingUp)
		{
			AttachTo(collision.gameObject);
			return;
		}

		// check speed
		float collisionSpeed = kinematicObj == null ? collision.relativeVelocity.magnitude : (m_body.velocity - kinematicObj.velocity).magnitude + Mathf.Abs(m_aimVelocity) + m_aimRadiusVelocity; // TODO: incorporate aim velocity direction?
		if (collisionSpeed > m_damageThresholdSpeed)
		{
			// play audio
			if (m_collisionAudio != null && m_collisionAudio.Length > 0)
			{
				m_audioSource.PlayOneShot(m_collisionAudio[Random.Range(0, m_collisionAudio.Length)]);
			}

			// if hitting a valid point, apply damage
			bool canDamage = collision.gameObject.GetComponent<AvatarController>() == null; // TODO: base on what object threw us
			if (canDamage)
			{
				Health otherHealth = collision.gameObject.GetComponent<Health>();
				if (otherHealth != null)
				{
					otherHealth.Decrement();
				}
				if (m_health != null)
				{
					m_health.Decrement();
				}
			}
		}

		// done for fully-dynamic collisions
		if (kinematicObj == null)
		{
			return;
		}

		// add upward force to emulate kicking
		List<ContactPoint2D> contacts = new List<ContactPoint2D>();
		int contactCount = collision.GetContacts(contacts);
		for (int i = 0; i < contactCount; ++i) // NOTE that we can't use foreach() since GetContacts() for some reason adds a bunch of null entries
		{
			ContactPoint2D pos = contacts[i];
			m_body.AddForceAtPosition(Vector2.up * collisionSpeed, pos.point);
		}
	}

	private float AimDegreesRaw(Vector3 position)
	{
		Vector2 aimDiff = position - transform.parent.position;
		return Mathf.Rad2Deg * Mathf.Atan2(aimDiff.y, aimDiff.x);
	}

	private float DampedSpring(float current, float target, float dampPct, bool isAngle, float stiffness, ref float velocityCurrent)
	{
		// spring motion: F = kx - dv, where x = {vel/pos}_desired - {vel/pos}_current
		// critically damped spring: d = 2*sqrt(km)
		float mass = m_body.mass;
		float dampingFactor = 2.0f * Mathf.Sqrt(m_aimSpringStiffness * mass) * dampPct;
		float diff = target - current;
		while (isAngle && Mathf.Abs(diff) > 180.0f)
		{
			diff -= diff < 0.0f ? -360.0f : 360.0f;
		}
		float force = stiffness * diff - dampingFactor * velocityCurrent;

		float accel = force / mass;
		velocityCurrent += accel * Time.deltaTime;

		return current + velocityCurrent * Time.deltaTime;
	}
}
