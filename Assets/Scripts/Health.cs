using Platformer.Gameplay;
using UnityEngine;
using UnityEngine.Assertions;
using static Platformer.Core.Simulation;


namespace Platformer.Mechanics
{
	/// <summary>
	/// Represebts the current vital statistics of some game entity.
	/// </summary>
	public class Health : MonoBehaviour
	{
		/// <summary>
		/// The maximum hit points for the entity.
		/// </summary>
		public int maxHP = 1;

		public GameObject m_healthUIParent;
		public Sprite m_healthSprite;
		public Sprite m_healthMissingSprite;
		public float m_UIPadding = 5.0f;

		public bool m_invincible;
		public const float m_invincibilityTime = 1.0f; // TODO: vary by character type / animation played?


		/// <summary>
		/// Indicates if the entity should be considered 'alive'.
		/// </summary>
		public bool IsAlive => currentHP > 0;


		float currentHP;

		private Animator m_animator;
		private AnimationController m_character;


		/// <summary>
		/// Increment the HP of the entity.
		/// </summary>
		public bool Increment(int amount = 1)
		{
			bool changed = IncrementInternal(amount);
			SyncUI();
			return changed;
		}

		/// <summary>
		/// Decrement the HP of the entity. Will trigger a HealthIsZero event when
		/// current HP reaches 0.
		/// </summary>
		public bool Decrement(GameObject source, float amount = 1)
		{
			if (m_invincible)
			{
				return false;
			}

			IncrementInternal(-1.0f * amount);
			if (m_animator != null)
			{
				m_animator.SetTrigger("hurt");
			}
			bool isDead = Mathf.Approximately(currentHP, 0.0f);
			if (m_character != null)
			{
				m_character.OnDamage(source);
				if (isDead)
				{
					m_character.OnDeath();
				}
			}

			m_invincible = true;
			if (!isDead)
			{
				Schedule<EnableDamage>(m_invincibilityTime).m_health = this;
			}

			SyncUI();

			return true;
		}

		/// <summary>
		/// Decrement the HP of the entitiy until HP reaches 0.
		/// </summary>
		public void Die()
		{
			Decrement(gameObject, currentHP); // NOTE that we can't use IncrementInternal() directly since that skips the death logic
		}

		/// <summary>
		/// Increment the HP of the entitiy until HP reaches max.
		/// </summary>
		public void Respawn()
		{
			IncrementInternal(maxHP - currentHP);
			SyncUI();
			m_invincible = false;
		}


		void Awake()
		{
			m_animator = GetComponent<Animator>();
			m_character = GetComponent<AnimationController>();
			currentHP = maxHP;
			SyncUI();
		}


		private bool IncrementInternal(float diff)
		{
			float hpPrev = currentHP;
			currentHP = Mathf.Clamp(currentHP + diff, 0, maxHP);
			return !Mathf.Approximately(currentHP, hpPrev);
		}

		private void SyncUI()
		{
			if (m_healthUIParent == null)
			{
				return;
			}

			// get current UI count
			int uiHealthCount = m_healthUIParent.transform.childCount - 1; // NOTE that we assume that the first child is a deactivated template object
			Assert.IsTrue(uiHealthCount >= 0);

			// remove excess
			for (; uiHealthCount > maxHP; --uiHealthCount)
			{
				Destroy(m_healthUIParent.transform.GetChild(uiHealthCount).gameObject);
			}

			// add deficient
			GameObject templateObj = m_healthUIParent.transform.GetChild(0).gameObject;
			float templateWidth = templateObj.GetComponent<RectTransform>().sizeDelta.x;
			float xItr = (templateWidth + m_UIPadding) * uiHealthCount;
			Assert.IsFalse(templateObj.activeSelf);
			for (; uiHealthCount < maxHP; ++uiHealthCount, xItr += templateWidth + m_UIPadding)
			{
				GameObject uiNew = Instantiate(templateObj, m_healthUIParent.transform);
				uiNew.transform.position += new Vector3(xItr, 0.0f, 0.0f);
				uiNew.SetActive(true);
			}

			// set sprites
			// TODO: handle partial hit points?
			for (int i = 1; i <= maxHP; ++i)
			{
				m_healthUIParent.transform.GetChild(i).GetComponent<UnityEngine.UI.Image>().sprite = i <= currentHP ? m_healthSprite : m_healthMissingSprite;
			}

			// TODO: adjust size of parent if its width is ever visible/used
		}
	}
}
