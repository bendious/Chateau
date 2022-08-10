using System.Linq;
using UnityEngine;


public interface IKey
{
	public Behaviour Component => this as Behaviour; // NOTE that this returns a Behaviour rather than a Component to give access to Behaviour.enabled

	public IUnlockable Lock { get; set; }

	public bool IsInPlace { get; set; }


	private const string m_spriteText = "<sprite index=0 tint=1>";


	public void SetCombination(LockController.CombinationSet set, int[] combination, int optionIndex, int indexCorrect, int startIndex, int endIndex, bool useSprites);

	public void Use();

	public void Deactivate();


	protected static System.Collections.Generic.IEnumerable<string> CombinationToText(LockController.CombinationSet set, int[] combination, int optionIndex, int startIndex, int endIndex) => Enumerable.Repeat(m_spriteText, startIndex).Concat(combination[startIndex .. endIndex].Select(idx => set.m_options[idx].m_strings[optionIndex])).Concat(Enumerable.Repeat(m_spriteText, combination.Length - endIndex));
}
