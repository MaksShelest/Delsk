using Simulation.Core;

namespace Agents.Algorithmic;

public sealed partial class AlgorithmicController
{
    private long InputTarget(ControllerObservation o,ControllerInventory input,RecipeDefinition recipe,MaterialCost ingredient)
    {
        if(recipe.Ingredients.Length==1)return _state.Settings.BatchQuantity;
        long volume=recipe.Ingredients.Sum(i=>checked(i.Quantity*Volume(o,i.ItemId)));
        long batches=Math.Max(1,Math.Min(input.Capacity/volume,_state.Settings.BatchQuantity/recipe.Ingredients.Sum(i=>i.Quantity)));
        return checked(batches*ingredient.Quantity);
    }
    private long InputSpace(ControllerObservation o,ControllerInventory input,RecipeDefinition recipe,string item)
    {
        long protectedVolume=recipe.Ingredients.Where(i=>i.ItemId!=item).Sum(i=>checked(Math.Max(0,i.Quantity-Free(input,i.ItemId))*Volume(o,i.ItemId)));
        return Math.Max(0,FreeVolume(input)-protectedVolume)/Volume(o,item);
    }
    private static long RecipeInputValue(ControllerObservation o,RecipeDefinition recipe)=>recipe.Ingredients.Sum(i=>checked(i.Quantity*BasePrice(o,i.ItemId)));
}
