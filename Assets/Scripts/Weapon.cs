using System;

[Serializable]
public class Weapon
{
    public string id;
    public float damage;
    public float cooldown;

    public Weapon(string id, float damage, float cooldown)
    {
        this.id = id;
        this.damage = damage;
        this.cooldown = cooldown;
    }

    public override string ToString()
        => $"Weapon[{id}] damage={damage} cooldown={cooldown}";
}