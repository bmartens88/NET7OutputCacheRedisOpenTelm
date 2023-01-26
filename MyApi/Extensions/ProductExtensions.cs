using MyApi.Data.Model;
using MyApi.Dto;

namespace MyApi.Extensions;

public static class ProductExtensions
{
    public static ProductDto AsDto(this Product product)
    {
        return new ProductDto(
            product.Name,
            product.Description,
            product.Price);
    }
}