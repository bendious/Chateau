using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class Spring : MonoBehaviour
{
	public float m_launchDistance;

	[SerializeField] private float m_dampPct = 0.1f;
	[SerializeField] private float m_stiffness = 1000.0f;
	[SerializeField] private float m_mass = 1.0f;
	[SerializeField] private float m_heightMin = 0.25f;

	[SerializeField] private float m_speedThreshold = 5.0f;
	[SerializeField] private WeightedObject<AudioClip>[] m_sfxFast;
	[SerializeField] private WeightedObject<AudioClip>[] m_sfxSlow;


	private SpriteRenderer m_renderer;
	private BoxCollider2D m_collider;
	private AudioSource m_audio;

	private float m_heightInitial;
	private float m_offsetPct;
	private float m_diffMin;
	private float m_diff;
	private float m_diffVel;
	private float m_diffRest;

	private readonly HashSet<Rigidbody2D> m_supportedBodies = new();
	private bool m_sfxIsFast;


	private void Awake()
	{
		m_renderer = GetComponent<SpriteRenderer>();
		m_collider = GetComponent<BoxCollider2D>();
		m_audio = GetComponent<AudioSource>();
		m_heightInitial = m_collider.size.y; // TODO: don't assume the renderer and collider heights are the same?
		m_offsetPct = m_collider.offset.y / m_heightInitial;
		m_diffMin = -m_heightInitial + m_heightMin;
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		if (m_launchDistance == 0.0f || collision.rigidbody == null || collision.rigidbody.bodyType == RigidbodyType2D.Static || collision.transform.parent != null)
		{
			return;
		}

		// ignore collisions at base of spring
		// TODO: parameterize?
		Vector2 collisionPos = collision.GetContact(0).point; // TODO: handle multi-contacts? use lowest y-value from collider?
		if (collisionPos.y < collision.otherCollider.bounds.center.y)
		{
			return;
		}

		m_supportedBodies.Add(collision.rigidbody);
	}

	private void OnCollisionExit2D(Collision2D collision)
	{
		MaybeLaunch(collision.rigidbody);
		m_supportedBodies.Remove(collision.rigidbody);
	}

	private void FixedUpdate()
	{
		if (m_supportedBodies.Count <= 0 && m_diff.FloatEqual(0.0f) && m_diffVel.FloatEqual(0.0f))
		{
			if (m_audio.isPlaying)
			{
				m_audio.Stop();
			}
			return;
		}

		// calculate
		float massTotal = m_mass + m_supportedBodies.Sum(b => b == null ? 0.0f : b.mass);
		m_diffRest = massTotal * -Physics2D.gravity.magnitude / m_stiffness; // force = stiffness * diff, force = mass * accel = mass * gravity --> diff = mass * gravity / stiffness
		m_diff = Utility.DampedSpring(m_diff, m_diffRest, m_dampPct, false, m_stiffness, m_mass, ref m_diffVel);
		if (m_diff < m_diffMin)
		{
			m_diff = m_diffMin;
			m_diffVel = Mathf.Max(m_diffVel, 0.0f);
		}

		// resize
		float heightCur = m_heightInitial + m_diff;
		m_renderer.size = new(m_renderer.size.x, heightCur);
		float colliderDiff = heightCur - m_collider.size.y;
		if (!colliderDiff.FloatEqual(0.0f))
		{
			// make sure supported bodies stay in contact w/ our collider as it shrinks
			// NOTE that this occurs BEFORE setting m_collider.{size/offset}
			List<Collider2D> colliders = new();
			foreach (Rigidbody2D body in m_supportedBodies)
			{
				if (body == null) // in case of destroyed objects
				{
					continue;
				}
				KinematicObject kObj = body.GetComponent<KinematicObject>();
				float upwardSpeed = kObj == null ? body.velocity.y : kObj.velocity.y;
				if (m_diffVel < 0.0f || m_diffVel > upwardSpeed)
				{
					body.GetAttachedColliders(colliders);
					Bounds bounds = colliders.ToBounds();
					float bodyDiff = m_collider.bounds.max.y + colliderDiff - bounds.min.y;
					body.position = new(body.position.x, body.position.y + bodyDiff);
				}
			}
		}
		m_collider.size = new(m_collider.size.x, heightCur);
		m_collider.offset = new(m_collider.offset.x, heightCur * m_offsetPct);

		// SFX
		bool isFast = Mathf.Abs(m_diffVel) >= m_speedThreshold;
		if (!m_audio.isPlaying || isFast != m_sfxIsFast)
		{
			WeightedObject<AudioClip>[] clips = isFast ? m_sfxFast : m_sfxSlow;
			if (clips.Length > 0)
			{
				m_audio.clip = clips.RandomWeighted();
				m_audio.Play();
			}
			m_sfxIsFast = isFast;
		}

		// TODO: VFX?
	}


	private void MaybeLaunch(Rigidbody2D body)
	{
		if (!m_supportedBodies.Contains(body) || m_diff < m_diffRest || m_diffVel < m_speedThreshold)
		{
			return;
		}

		// check object's velocity to avoid erroneous launches
		KinematicObject kObj = body.GetComponent<KinematicObject>();
		float yVel = (kObj == null ? body.velocity : kObj.velocity).y;
		if (m_diffVel < yVel) // TODO: don't assume vertical orientation
		{
			return;
		}

		// calculate launch velocity
		// TODO: take player inputs into account?
		float twoG = 2.0f * Physics2D.gravity.magnitude;
		float launchSpeed = Mathf.Sqrt(twoG * m_launchDistance); // v0 = sqrt(2*g*y); https://math.stackexchange.com/questions/785375/calculate-initial-velocity-to-reach-height-y // TODO: don't assume pure vertical launch?
		Vector2 launchVel = transform.rotation * Vector2.up * launchSpeed;

		// add velocity
		switch (body.bodyType)
		{
			case RigidbodyType2D.Dynamic:
				List<ContactPoint2D> contacts = new();
				body.GetContacts(contacts);
				Vector2 contactPos = contacts.FirstOrDefault(cp => cp.collider == m_collider).point;
				Vector2 collisionPos = contactPos == Vector2.zero ? (body.transform.position + transform.position) * 0.5f : contactPos; // TODO: don't assume contact will never be at the origin?
				body.AddForceAtPosition(launchVel, collisionPos); // NOTE that we don't take into account the mass of the object since dynamic launches don't need to be as precise and feel better when not mass-independent
				break;
			case RigidbodyType2D.Kinematic:
				float decayTime = launchSpeed / twoG; // since y = v0*t - g*t^2, y' = v = v0 - 2*g*t, so when v=0, t = v0 / 2*g
				kObj.Bounce(launchVel, decayTime, decayTime);
				break;
			default:
				Debug.LogWarning("Unrecognized body type on spring?");
				break;
		}

		// disable collision to prevent multi-launching
		List<Collider2D> colliders = new();
		body.GetAttachedColliders(colliders);
		EnableCollision.TemporarilyDisableCollision(new[] { m_collider }, colliders);

		m_supportedBodies.Remove(body);
	}
}
