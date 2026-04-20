namespace src.AI;

public interface IModelMetricsStore
{
	ModelMetricsSnapshot Get();
	void Save(ModelMetricsSnapshot snapshot);
}