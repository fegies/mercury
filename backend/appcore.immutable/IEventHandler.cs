namespace appcore.immutable;

public interface IEventHandler
{

    public Decision HandleEvent(List<Event> inputs);

}


