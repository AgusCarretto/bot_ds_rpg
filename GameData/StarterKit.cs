namespace BotDsRpg.GameData;

// Lo que se le regala a una cuenta NUEVA al hacer /start (v0.14.6). En nivel 1 una pelea cuesta entre el 30 % y el 53 % de la vida, la vida no se recupera sola y curar esa pelea con comida sale
// más caro de lo que la pelea paga (medido con el resolver real: ~19 de oro de Mate Amargo contra ~12 de premio): sin esto, la segunda pelea seguida de alguien que todavía no sabe curarse
// se pierde entre el 8 % y el 28 % de las veces. Cinco Mate Amargo (8 de oro cada uno, +15 HP) le dan unos 75 HP para aprender a jugar. Es UNA vez por cuenta: no se vuelve a dar con el
// Fuego Nuevo ni al cambiar de clase (UserRepository.CreateAccountAsync lo entrega solo cuando la fila de users es realmente nueva).
public static class StarterKit
{
    // El nombre exacto del ítem en la tabla items (Database/seed_consumables_and_base_swords.sql). Si no existe, la cuenta se crea igual y el kit simplemente no se entrega.
    public const string FoodItemName = "Mate Amargo";

    public const int FoodQuantity = 5;
}
