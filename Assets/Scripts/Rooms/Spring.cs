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

	private readonly HashSet<Rigidbody2D> m_supportedBodies = new();
	private bool m_isAnimating;
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
		if (!m_isAnimating)
		{
			StartCoroutine(Animate());
		}
	}

	private void OnCollisionExit2D(Collision2D collision) => m_supportedBodies.Remove(collision.rigidbody);


	private IEnumerator Animate()
	{
		m_isAnimating = true;

		float diff = 0.0f;
		float diffVel = 0.0f;
		while (m_supportedBodies.Count > 0 || !diff.FloatEqual(0.0f) || !diffVel.FloatEqual(0.0f))
		{
			// calculate
			float massTotal = m_supportedBodies.Sum(b => b == null ? 0.0f : b.mass);
			float restDiff = massTotal * -Physics2D.gravity.magnitude / m_stiffness; // force = stiffness * diff, force = mass * accel = mass * gravity --> diff = mass * gravity / stiffness
			diff = Utility.DampedSpring(diff, restDiff, m_dampPct, false, m_stiffness, m_mass, ref diffVel);
			if (diff < m_diffMin)
			{
				diff = m_diffMin;
				diffVel = Mathf.Max(diffVel, 0.0f);
			}

			// resize
			float heightCur = m_heightInitial + diff;
			m_renderer.size = new(m_renderer.size.x, heightCur);
			float colliderDiff = heightCur - m_collider.size.y;
			if (colliderDiff < 0.0f)
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
					body.GetAttachedColliders(colliders);
					Bounds bounds = colliders.ToBounds();
					float bodyDiff = m_collider.bounds.max.y + colliderDiff - bounds.min.y;
					if (bodyDiff < 0.0f)
					{
						body.position = new(body.position.x, body.position.y + bodyDiff);
					}
				}
			}
			m_collider.size = new(m_collider.size.x, heightCur);
			m_collider.offset = new(m_collider.offset.x, heightCur * m_offsetPct);

			// launch
			if (diff > restDiff && diffVel > m_speedThreshold)
			{
				// calculate launch velocity
				// TODO: take player inputs into account?
				float twoG = 2.0f * Physics2D.gravity.magnitude;
				float launchSpeed = Mathf.Sqrt(twoG * m_launchDistance); // v0 = sqrt(2*g*y); https://math.stackexchange.com/questions/785375/calculate-initial-velocity-to-reach-height-y // TODO: don't assume pure vertical launch?
				Vector2 launchVel = transform.rotation * Vector2.up * launchSpeed;

				foreach (Rigidbody2D body in m_supportedBodies)
				{
					if (body == null) // in case of destroyed objects
					{
						continue;
					}
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
							float decayTime = -launchSpeed / twoG; // since y = v0*t + g*t^2, y' = v = v0 + 2*g*t, so when v=0, t = -v0 / 2*g
							body.GetComponent<KinematicObject>().Bounce(launchVel, decayTime, decayTime);
							break;
						default:
							Debug.LogWarning("Unrecognized body type on spring?");
							break;
					}
				}
				m_supportedBodies.Clear();
			}

			// SFX
			bool isFast = Mathf.Abs(diffVel) >= m_speedThreshold;
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

			yield return null;
		}

		m_audio.Stop();
		m_isAnimating = false;
	}
}
