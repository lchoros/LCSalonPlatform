using NSubstitute;

using Salon.Web.Framework.Pipeline;

namespace Salon.Tests.Unit.Pipeline;

public class TransactionBehaviorTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Command_runs_inside_a_transaction_that_is_committed()
    {
        var order = new List<string>();
        _unitOfWork.HasActiveTransaction.Returns(false);
        _unitOfWork.When(u => u.BeginTransactionAsync(Arg.Any<CancellationToken>())).Do(_ => order.Add("begin"));
        _unitOfWork.When(u => u.CommitAsync(Arg.Any<CancellationToken>())).Do(_ => order.Add("commit"));
        var behavior = new TransactionBehavior<Ping, string>(_unitOfWork);

        var result = await behavior.HandleAsync(
            new Ping("x"),
            () =>
            {
                order.Add("handler");
                return Task.FromResult("ok");
            },
            CancellationToken.None);

        result.ShouldBe("ok");
        order.ShouldBe(["begin", "handler", "commit"]);
        await _unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failing_command_rolls_back_and_rethrows()
    {
        _unitOfWork.HasActiveTransaction.Returns(false);
        var behavior = new TransactionBehavior<Ping, string>(_unitOfWork);

        await Should.ThrowAsync<InvalidOperationException>(
            () => behavior.HandleAsync(new Ping("x"), () => throw new InvalidOperationException("boom"), CancellationToken.None));

        await _unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Command_joins_an_already_open_transaction()
    {
        _unitOfWork.HasActiveTransaction.Returns(true);
        var behavior = new TransactionBehavior<Ping, string>(_unitOfWork);

        (await behavior.HandleAsync(new Ping("x"), () => Task.FromResult("ok"), CancellationToken.None)).ShouldBe("ok");

        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Query_does_not_open_a_transaction()
    {
        _unitOfWork.HasActiveTransaction.Returns(false);
        var behavior = new TransactionBehavior<CountThings, int>(_unitOfWork);

        (await behavior.HandleAsync(new CountThings(), () => Task.FromResult(7), CancellationToken.None)).ShouldBe(7);

        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }
}