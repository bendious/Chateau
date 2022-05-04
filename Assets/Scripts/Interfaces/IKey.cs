using UnityEngine;


public interface IKey
{
	public Component Component => this as Component;

	public IUnlockable Lock { get; set; }

	public bool IsInPlace { get; set; }


	public void Use();

	public void Deactivate();
}
