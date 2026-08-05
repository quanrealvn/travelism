import { describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ExpensePanel } from './ExpensePanel'
import type {
  BalanceResponse,
  ExpenseResponse,
  MemberResponse,
} from '../api/api-types'

const QUAN = 'member-quan'
const LINH = 'member-linh'

const MEMBERS: MemberResponse[] = [
  { id: QUAN, displayName: 'Quan', role: 'Owner', createdAt: '2026-03-01T00:00:00+00:00' },
  { id: LINH, displayName: 'Linh', role: 'Editor', createdAt: '2026-03-01T00:00:00+00:00' },
]

function expense(overrides: Partial<ExpenseResponse> = {}): ExpenseResponse {
  return {
    id: 'e1',
    tripId: 't1',
    title: 'Xăng xe',
    amount: 100_000,
    currency: 'VND',
    paidByMemberId: QUAN,
    date: '2026-03-01',
    category: 'Transport',
    splitType: 'Equal',
    shares: [
      { memberId: QUAN, shareAmount: 50_000 },
      { memberId: LINH, shareAmount: 50_000 },
    ],
    createdAt: '2026-03-01T00:00:00+00:00',
    updatedAt: '2026-03-01T00:00:00+00:00',
    updatedByMemberId: QUAN,
    ...overrides,
  }
}

function renderPanel(
  overrides: Partial<Parameters<typeof ExpensePanel>[0]> = {},
) {
  const handlers = { onAdd: vi.fn(), onDelete: vi.fn() }

  const balance: BalanceResponse = {
    balances: [
      { memberId: QUAN, paid: 100_000, owed: 50_000, net: 50_000 },
      { memberId: LINH, paid: 0, owed: 50_000, net: -50_000 },
    ],
    transfers: [{ fromMemberId: LINH, toMemberId: QUAN, amount: 50_000 }],
    totalSpent: 100_000,
    currency: 'VND',
    currencyExponent: 0,
  }

  render(
    <ExpensePanel
      expenses={[expense()]}
      balance={balance}
      members={MEMBERS}
      myMemberId={QUAN}
      currency="VND"
      currencyExponent={0}
      tripDays={['2026-03-01', '2026-03-02']}
      pending={false}
      deletingId={null}
      submitError={null}
      {...handlers}
      {...overrides}
    />,
  )

  return handlers
}

describe('ExpensePanel settlement display', () => {
  it('names who pays whom and how much', () => {
    renderPanel()

    const settle = within(screen.getByLabelText('Cần thanh toán'))
    expect(settle.getByText(/Linh/)).toBeInTheDocument()
    expect(settle.getByText(/Quan/)).toBeInTheDocument()
    expect(settle.getByText(/50\.000/)).toBeInTheDocument()
  })

  it('shows a creditor as owed and a debtor as owing', () => {
    renderPanel()

    expect(screen.getByTestId(`balance-${QUAN}`)).toHaveTextContent(/được nhận/i)
    expect(screen.getByTestId(`balance-${LINH}`)).toHaveTextContent(/còn nợ/i)
  })

  it('shows a debt as a positive number rather than a minus sign', () => {
    // "còn nợ -50.000" would read as being owed money.
    renderPanel()

    const linh = screen.getByTestId(`balance-${LINH}`)
    expect(linh).toHaveTextContent(/50\.000/)
    expect(linh).not.toHaveTextContent(/-50\.000/)
  })

  it('says plainly when nobody owes anything', () => {
    renderPanel({
      balance: {
        balances: [
          { memberId: QUAN, paid: 50_000, owed: 50_000, net: 0 },
          { memberId: LINH, paid: 50_000, owed: 50_000, net: 0 },
        ],
        transfers: [],
        totalSpent: 100_000,
        currency: 'VND',
        currencyExponent: 0,
      },
    })

    expect(screen.getByText(/không ai nợ ai/i)).toBeInTheDocument()
    expect(screen.queryByLabelText('Cần thanh toán')).not.toBeInTheDocument()
  })

  it('formats money with the trip currency exponent', () => {
    renderPanel()

    // 100_000 minor units of a zero-decimal currency is ₫100.000, not ₫1.000,00.
    expect(screen.getByText(/Tổng chi/)).toHaveTextContent('100.000')
  })

  it('shows a loading state rather than a wrong zero while the balance is unknown', () => {
    renderPanel({ balance: undefined })

    expect(screen.getByText(/đang tính/i)).toBeInTheDocument()
  })
})

describe('ExpensePanel list and form', () => {
  it('lists an expense with who paid it', () => {
    renderPanel()

    const row = screen.getByTestId('expense-e1')
    expect(row).toHaveTextContent('Xăng xe')
    expect(row).toHaveTextContent(/Quan trả/)
  })

  it('submits an amount converted to integer minor units', async () => {
    const handlers = renderPanel()

    await userEvent.type(screen.getByLabelText(/nội dung/i), 'Bữa tối')
    await userEvent.type(screen.getByLabelText(/số tiền/i), '250000')
    await userEvent.click(screen.getByRole('button', { name: /thêm khoản chi/i }))

    expect(handlers.onAdd).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'Bữa tối', amount: 250_000, splitType: 'Equal' }),
    )
  })

  it('refuses a zero amount before reaching the server', async () => {
    const handlers = renderPanel()

    await userEvent.type(screen.getByLabelText(/nội dung/i), 'Miễn phí')
    await userEvent.type(screen.getByLabelText(/số tiền/i), '0')
    await userEvent.click(screen.getByRole('button', { name: /thêm khoản chi/i }))

    expect(handlers.onAdd).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent(/lớn hơn 0/i)
  })

  it('refuses an unparseable amount', async () => {
    const handlers = renderPanel()

    await userEvent.type(screen.getByLabelText(/nội dung/i), 'Không rõ')
    await userEvent.type(screen.getByLabelText(/số tiền/i), 'nhiều lắm')
    await userEvent.click(screen.getByRole('button', { name: /thêm khoản chi/i }))

    expect(handlers.onAdd).not.toHaveBeenCalled()
  })

  it('asks to delete the expense whose button was pressed', async () => {
    const handlers = renderPanel()

    await userEvent.click(screen.getByLabelText(/xoá xăng xe/i))

    expect(handlers.onDelete).toHaveBeenCalledWith('e1')
  })
})
