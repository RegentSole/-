using System;

[Serializable]
public class WeaponDto
{
    public string id;
    public float damage;
    public float cooldown;
}

[Serializable]
public class WeaponListDto
{
    public WeaponDto[] items;
}