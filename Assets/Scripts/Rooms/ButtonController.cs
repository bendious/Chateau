using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class ButtonController : MonoBehaviour, IInteractable, IKey
{
	[SerializeField] private bool m_reusable = false;
	[SerializeField] private float m_unactiveColorPct = 0.5f;
	[SerializeField] private float m_activeColorPct = 2.0f;


	public IUnlockable Lock { get; set; }

	public bool IsInPlace { get; set; }


	private SpriteRenderer m_renderer;


	private void Awake()
	{
		m_renderer = GetComponent<SpriteRenderer>();
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled && (m_reusable || !IsInPlace);

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		Use();
	}


	public void SetCombination(LockController.CombinationSet set, int[] combination, int optionIndex, int indexCorrect, int startIndex, int endIndex, bool useSprites) => Debug.LogError("Can't SetCombination() on ButtonController");

	public void Use()
	{
		UpdateWireColor(true); // NOTE that this is BEFORE Lock.Unlock() in case it Cancel()s us, resetting colors
		Lock.Unlock(this);
		if (!m_reusable)
		{
			Deactivate();
		}
	}

	public void Cancel()
	{
		IsInPlace = false; // TODO: use base(IKey).Cancel() if Unity ever updates the compiler enough to support it

		UpdateWireColor(false);
	}

	public void Deactivate()
	{
		if (!enabled)
		{
			return; // to avoid multiplying colors by m_unactiveColorPct multiple times
		}
		enabled = false;

		// leave non-item keys in place, just turning off their light/text
		GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;

		UpdateWireColor(true);
		m_renderer.color *= m_unactiveColorPct;
	}


	private void UpdateWireColor(bool active)
	{
		LineRenderer line = GetComponent<LineRenderer>();
		line.colorGradient = new Gradient() { colorKeys = new GradientColorKey[] { new GradientColorKey(m_renderer.color * (active ? m_activeColorPct : m_unactiveColorPct), 0.0f) }, alphaKeys = new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f) } };
	}
}
