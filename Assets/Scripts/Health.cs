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

		/// <summary>
		/// Indicates if the entity should be considered 'alive'.
		/// </summary>
		public bool IsAlive => currentHP > 0;


		int currentHP;

		private bool m_invincible;
		private const float m_invincibilityTime = 0.5f; // TODO: vary by character type / animation played?

		private Animator m_animator;
		private AnimationController m_character;


		/// <summary>
		/// Increment the HP of the entity.
		/// </summary>
		public void Increment()
		{
			IncrementInternal(1);
			SyncUI();
		}

		/// <summary>
		/// Decrement the HP of the entity. Will trigger a HealthIsZero event when
		/// current HP reaches 0.
		/// </summary>
		public bool Decrement()
		{
			if (m_invincible)
			{
				return false;
			}

			IncrementInternal(-1);
			if (m_animator != null)
			{
				m_animator.SetTrigger("hurt");
			}
			if (m_character != null)
			{
				m_character.OnDamage();
				if (currentHP == 0)
				{
					m_character.OnDeath();
				}
			}
			else
			{
				m_invincible = true;
				Schedule<EnableDamage>(m_invincibilityTime).m_health = this;
			}
			SyncUI();

			return true;
		}

		public void EnableDamage()
		{
			m_invincible = false;
		}

		/// <summary>
		/// Decrement the HP of the entitiy until HP reaches 0.
		/// </summary>
		public void Die()
		{
			IncrementInternal(-currentHP - 1); // NOTE that we have to go to one and then use Decrement() to avoid skipping the death logic there
			Decrement();
		}

		/// <summary>
		/// Increment the HP of the entitiy until HP reaches max.
		/// </summary>
		public void Respawn()
		{
			IncrementInternal(maxHP - currentHP);
			SyncUI();
		}


		void Awake()
		{
			m_animator = GetComponent<Animator>();
			m_character = GetComponent<AnimationController>();
			currentHP = maxHP;
			SyncUI();
		}


		private void IncrementInternal(int diff)
		{
			currentHP = Mathf.Clamp(currentHP + diff, 0, maxHP);
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
			for (int i = 1; i <= maxHP; ++i)
			{
				m_healthUIParent.transform.GetChild(i).GetComponent<UnityEngine.UI.Image>().sprite = i <= currentHP ? m_healthSprite : m_healthMissingSprite;
			}

			// TODO: adjust size of parent if its width is ever visible/used
		}
	}
}
